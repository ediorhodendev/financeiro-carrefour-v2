using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
namespace ServiceDefaults;

public static class WebDefaults
{
 public static void ConfigureTelemetry(this IHostApplicationBuilder builder,string name)
 {
    builder.Services.AddSerilog(c=>c.MinimumLevel.Information().MinimumLevel.Override("Microsoft",Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore",Serilog.Events.LogEventLevel.Fatal)
        .Enrich.FromLogContext().WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter()));
    var otel=builder.Services.AddOpenTelemetry().ConfigureResource(r=>r.AddService(name))
        .WithTracing(t=>{t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();if(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is not null)t.AddOtlpExporter();})
        .WithMetrics(m=>{m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();if(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is not null)m.AddOtlpExporter();});
 }
 public static void AddBankApi(this WebApplicationBuilder builder,string name)
 {
    builder.ConfigureTelemetry(name);
    builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=65536);
    builder.Services.AddControllers();builder.Services.AddProblemDetails(o=>o.CustomizeProblemDetails=c=>{
        c.ProblemDetails.Type="https://www.rfc-editor.org/rfc/rfc9457";
        c.ProblemDetails.Extensions["traceId"]=Activity.Current?.Id??c.HttpContext.TraceIdentifier;
    });
    builder.Services.AddExceptionHandler<SafeExceptionHandler>();
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>{
        o.MapInboundClaims=false;
        var authority=builder.Configuration["Jwt:Authority"];
        if(!string.IsNullOrWhiteSpace(authority)){o.Authority=authority;o.Audience=builder.Configuration["Jwt:Audience"];}
        else
        {
            if(!builder.Environment.IsDevelopment())throw new InvalidOperationException("Production requires a JWT authority.");
            var key=builder.Configuration["Jwt:SigningKey"]??throw new InvalidOperationException("JWT signing key required.");
            if(key.Length<32)throw new InvalidOperationException("JWT key must contain at least 32 characters.");
            o.TokenValidationParameters=new(){ValidateIssuer=true,ValidateAudience=true,ValidateLifetime=true,ValidateIssuerSigningKey=true,
                ValidIssuer="digital-bank-demo",ValidAudience="digital-bank",IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),ClockSkew=TimeSpan.FromSeconds(5)};
        }
        o.Events=new JwtBearerEvents{
            OnChallenge=async c=>{c.HandleResponse();c.Response.StatusCode=401;await c.Response.WriteAsJsonAsync(new ProblemDetails{Status=401,Title="Authentication required.",Type="https://www.rfc-editor.org/rfc/rfc9457"},c.HttpContext.RequestAborted);},
            OnForbidden=async c=>{c.Response.StatusCode=403;await c.Response.WriteAsJsonAsync(new ProblemDetails{Status=403,Title="Insufficient scope.",Type="https://www.rfc-editor.org/rfc/rfc9457"},c.HttpContext.RequestAborted);}};
    });
    builder.Services.AddAuthorization(o=>{foreach(var scope in new[]{"ledger.write","balance.read"})o.AddPolicy(scope,p=>p.RequireAuthenticatedUser().RequireClaim("sub").RequireAssertion(c=>c.User.FindAll("scope").SelectMany(x=>x.Value.Split(' ')).Contains(scope)));});
    builder.Services.AddRateLimiter(o=>{
        o.GlobalLimiter=PartitionedRateLimiter.Create<HttpContext,string>(c=>RateLimitPartition.GetFixedWindowLimiter(c.User.FindFirst("sub")?.Value??c.Connection.RemoteIpAddress?.ToString()??"anonymous",
            _=>new(){PermitLimit=500,Window=TimeSpan.FromMinutes(1),QueueLimit=0,AutoReplenishment=true}));
        o.OnRejected=async(c,ct)=>{c.HttpContext.Response.StatusCode=429;c.HttpContext.Response.Headers.RetryAfter="60";await c.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails{Status=429,Title="Rate limit exceeded."},ct);};
    });
    builder.Services.AddOpenApi(o=>{o.AddDocumentTransformer((document,_,_)=>{
        document.Components??=new();document.Components.SecuritySchemes??=new Dictionary<string,IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"]=new OpenApiSecurityScheme{Type=SecuritySchemeType.Http,Scheme="bearer",BearerFormat="JWT"};
        document.Security=[new OpenApiSecurityRequirement{[new OpenApiSecuritySchemeReference("Bearer",document)]=[]}];
        return Task.CompletedTask;
    });
    o.AddOperationTransformer((operation,context,_)=>{
        if(context.Description.ActionDescriptor is Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor action
            && action.MethodInfo.GetCustomAttributes(true).Any(a=>a.GetType().Name=="IdempotentAttribute"))
        {
            operation.Parameters??=[];
            operation.Parameters.Add(new OpenApiParameter{Name="Idempotency-Key",In=ParameterLocation.Header,Required=true,
                Description="Reuse the exact body and key for retries. Retained for 48 hours.",Schema=new OpenApiSchema{Type=JsonSchemaType.String,MaxLength=128}});
        }
        return Task.CompletedTask;
    });});
 }
 public static void UseBankApi(this WebApplication app)
 {
    app.UseExceptionHandler();app.UseStatusCodePages(async context=>{
        var response=context.HttpContext.Response;await response.WriteAsJsonAsync(new ProblemDetails{Status=response.StatusCode,Title="Request rejected.",Type="https://www.rfc-editor.org/rfc/rfc9457"});});
    app.Use(async(c,next)=>{c.Request.EnableBuffering();c.Response.Headers["X-Correlation-ID"]=c.TraceIdentifier;await next(c);});
    app.UseAuthentication();app.UseAuthorization();app.UseRateLimiter();
    // Avoid logging raw URL/query, customer IDs, JWTs or free-text payloads.
    app.Use(async(c,next)=>{var watch=Stopwatch.StartNew();await next(c);Log.Information("HTTP {Method} {Status} {ElapsedMs}ms",c.Request.Method,c.Response.StatusCode,watch.Elapsed.TotalMilliseconds);});
    app.MapControllers();
    if(app.Environment.IsDevelopment()){app.MapOpenApi();app.MapScalarApiReference();}
 }
}
public sealed class SafeExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
 public async ValueTask<bool> TryHandleAsync(HttpContext http,Exception error,CancellationToken ct)
 {
    if(error is OperationCanceledException && ct.IsCancellationRequested)return false;
    var name=error.GetType().Name;
    var status=error is ArgumentException?400:name=="ResourceMissing"?404:name=="RuleViolation"?409:503;
    http.Response.StatusCode=status;
    return await problems.TryWriteAsync(new(){HttpContext=http,ProblemDetails=new(){Status=status,Title=status switch{400=>"Invalid request.",404=>"Resource not found.",409=>"Operation conflicts with financial rules.",_=>"Service temporarily unavailable; retry safely."},Detail=status is 400 or 409?error.Message:null}});
 }
}
