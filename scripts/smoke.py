"""Black-box evaluation of the three real Compose processes. Standard library only."""
import json,time,urllib.request,urllib.error,uuid,subprocess
from decimal import Decimal
LEDGER='http://localhost:5081'; BALANCE='http://localhost:5082'
def call(base,path,body=None,key=None,token=None):
    headers={'Content-Type':'application/json'}
    if key:headers['Idempotency-Key']=key
    if token:headers['Authorization']='Bearer '+token
    data=json.dumps(body,separators=(',',':')).encode() if body is not None else None
    req=urllib.request.Request(base+path,data=data,headers=headers)
    try:
        with urllib.request.urlopen(req,timeout=20) as response:return response.status,response.read()
    except urllib.error.HTTPError as error:return error.code,error.read()
def wait_ready(base):
    for _ in range(90):
        try:
            if call(base,'/health/ready')[0]==200:return
        except (OSError,urllib.error.URLError):pass
        time.sleep(1)
    raise AssertionError('API did not become ready')
wait_ready(LEDGER);wait_ready(BALANCE)
_,raw=call(LEDGER,'/demo/access',{});access=json.loads(raw);token=access['token']
_,raw=call(LEDGER,'/api/v1/contas',{},token=token);account=json.loads(raw)['id'];path='/api/v1/contas/'+account
key=str(uuid.uuid4());body={'tipo':'Credito','valor':100,'descricao':'Demo credit'}
status,credit=call(LEDGER,path+'/lancamentos',body,key,token);assert status==201,(status,credit)
assert call(LEDGER,path+'/lancamentos',body,key,token)==(201,credit)
assert call(LEDGER,path+'/lancamentos',dict(body,valor=101),key,token)[0]==422
status,debit=call(LEDGER,path+'/lancamentos',{'tipo':'Debito','valor':30,'descricao':'Demo debit'},str(uuid.uuid4()),token);assert status==201
original=json.loads(debit)['id'];revkey=str(uuid.uuid4());revpath=path+'/lancamentos/'+original+'/estorno'
status,reversal=call(LEDGER,revpath,{'motivo':'Correction'},revkey,token);assert status==201,(status,reversal)
assert call(LEDGER,revpath,{'motivo':'Correction'},revkey,token)==(201,reversal)
assert call(LEDGER,revpath,{'motivo':'Correction'},str(uuid.uuid4()),token)[0]==409
for _ in range(100):
    status,raw=call(BALANCE,path+'/saldo',token=token)
    if status==200 and json.loads(raw,parse_float=Decimal)['amount']==Decimal('100'):break
    time.sleep(.2)
else:raise AssertionError('Projection did not catch up')
# A broker outage must not prevent a financial commit. Projection catches up after restart.
subprocess.run(['docker','compose','stop','rabbitmq'],check=True)
assert call(LEDGER,path+'/lancamentos',{'tipo':'Credito','valor':5,'descricao':'Broker outage'},str(uuid.uuid4()),token)[0]==201
subprocess.run(['docker','compose','start','rabbitmq'],check=True)
for _ in range(120):
    status,raw=call(BALANCE,path+'/saldo',token=token)
    if status==200 and json.loads(raw,parse_float=Decimal)['amount']==Decimal('105'):break
    time.sleep(.5)
else:raise AssertionError('Projection did not recover')
print('PASS: Controllers, exact idempotent replay, full reversal, read projection, broker outage and recovery. Final balance 105 BRL.')
