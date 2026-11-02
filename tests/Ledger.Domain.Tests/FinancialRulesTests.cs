using Ledger.Domain;
using Ledger.Application;
using Balance.Domain;
using NSubstitute;
using Shouldly;
using Xunit;
namespace Ledger.Domain.Tests;
public sealed class FinancialRulesTests
{
    [Theory][InlineData(0)][InlineData(-1)][InlineData(0.00001)]
    public void RejectsInvalidAmounts(decimal value) => Should.Throw<ArgumentException>(() => Account.ValidateAmount(value));
    [Fact] public void MoneyUsesFourDecimals()
    {
        var account = NewAccount(); account.Apply(EntryType.Credit, 10.1234m); account.Apply(EntryType.Debit, 0.1234m);
        account.Balance.ShouldBe(10m); account.Sequence.ShouldBe(2);
    }
    [Fact] public void OverdraftDoesNotChangeState()
    {
        var a = NewAccount(); Should.Throw<RuleViolation>(() => a.Apply(EntryType.Debit, 1));
        a.Balance.ShouldBe(0); a.Sequence.ShouldBe(0);
    }
    [Fact] public void OverflowIsRejected()
    {
        var a = NewAccount(); a.Apply(EntryType.Credit, Account.Maximum);
        Should.Throw<RuleViolation>(() => a.Apply(EntryType.Credit, .0001m)); a.Balance.ShouldBe(Account.Maximum);
    }
    [Theory][InlineData(0)][InlineData(3)]
    public void UnknownTypeIsRejected(int type) => Should.Throw<ArgumentException>(() => NewAccount().Apply((EntryType)type, 1));
    [Theory][InlineData(EntryType.Credit,EntryType.Debit)][InlineData(EntryType.Debit,EntryType.Credit)]
    public void ReversalCompensatesAndPreservesOriginal(EntryType type, EntryType opposite)
    {
        var e = new Entry(Guid.NewGuid(), 1, type, 42.1234m, "Original", null, DateTimeOffset.UtcNow);
        var reverse = e.Reverse(2, "Correction", DateTimeOffset.UtcNow);
        reverse.Amount.ShouldBe(e.Amount); reverse.Type.ShouldBe(opposite); reverse.ReversalOf.ShouldBe(e.Id); e.ReversalOf.ShouldBeNull();
        Should.Throw<RuleViolation>(() => reverse.Reverse(3, "Again", DateTimeOffset.UtcNow));
    }
    [Theory][InlineData("")][InlineData(" ")]
    public void ReversalRequiresReason(string reason)
    {
        var e = new Entry(Guid.NewGuid(),1,EntryType.Credit,1,"Original",null,DateTimeOffset.UtcNow);
        Should.Throw<ArgumentException>(()=>e.Reverse(2,reason,DateTimeOffset.UtcNow));
    }
    [Fact] public void HashBindsBodyAndOperation()
    {
        IdempotencyHash.Compute("entry","{}").ShouldBe(IdempotencyHash.Compute("entry","{}"));
        IdempotencyHash.Compute("entry","{}").ShouldNotBe(IdempotencyHash.Compute("reversal","{}"));
        IdempotencyHash.Compute("entry","{}").ShouldNotBe(IdempotencyHash.Compute("entry","{ }"));
        IdempotencyHash.Compute("entry","{}").Length.ShouldBe(64);
    }
    [Theory][InlineData(null)][InlineData("")][InlineData(" ")][InlineData("bad\nkey")]
    public void InvalidKeysAreRejected(string? key) => Should.Throw<ArgumentException>(()=>IdempotencyHash.ValidateKey(key));
    [Fact] public void KeyLengthIsBounded()
    {
        IdempotencyHash.ValidateKey(new string('a',128)); Should.Throw<ArgumentException>(()=>IdempotencyHash.ValidateKey(new string('a',129)));
    }
    [Fact] public void ProjectionCannotSkipSequence() => Should.Throw<InvalidOperationException>(()=>ProjectionPosition.RequireNext(1,3));
    [Fact] public async Task HandlerUsesSessionAbstraction()
    {
        var session=Substitute.For<ILedgerSession>(); var a=NewAccount();
        session.LockAccountAsync(a.Id,a.CustomerId,Arg.Any<CancellationToken>()).Returns(a);
        session.RegistrationTimeAsync(Arg.Any<CancellationToken>()).Returns(DateTimeOffset.UtcNow);
        var result=await new RegisterEntryHandler(session).HandleAsync(new(a.Id,a.CustomerId,EntryType.Credit,10,"Credit",null),default);
        result.Amount.ShouldBe(10); session.Received(1).Add(Arg.Any<Entry>(),a);
    }
    private static Account NewAccount()=>new(Guid.NewGuid(),"demo-customer",DateTimeOffset.UtcNow);
}
