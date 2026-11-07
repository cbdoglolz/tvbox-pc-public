from pathlib import Path
import json,hashlib,subprocess,uuid
base=Path('.tools/update-installer-tests').resolve()/uuid.uuid4().hex
base.mkdir(parents=True)
helper=Path('windows/TVBox.Windows/Assets/updates/install-update.ps1').resolve()
import os
ps=str(Path(os.environ['SystemRoot'])/'System32/WindowsPowerShell/v1.0/powershell.exe')
def scenario(name, mode):
 root=base/name; target=root/'app'; operation=root/'operation'; stage=operation/'files';backup=operation/'backup'
 target.mkdir(parents=True);stage.mkdir(parents=True)
 original={'TVBox.exe':b'old-exe','libs/TVBox.dll':b'old-dll','prefs.json':b'private-fixture','history.json':b'private-progress','runtime/host/fxr/9/hostfxr.dll':b'old-runtime'}
 for name,body in original.items():
  path=target/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(body)
 payload={'TVBox.exe':b'new-exe','libs/TVBox.dll':b'new-dll','libs/new.dll':b'new-dependency'}
 if mode=='rollback':
  (target/'libs/blocked').write_bytes(b'must-preserve');payload['libs/blocked/new.dll']=b'blocked'
 if mode=='private': payload['prefs.json']=b'bad-overwrite'
 files=[]
 for name,body in payload.items():
  path=stage/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(body)
  files.append({'Path':name,'Sha256':hashlib.sha256(body).hexdigest() if mode!='hash' else '0'*64})
 plan={'Version':'9.0.0','InstallRoot':str(target),'StageRoot':str(stage),'BackupRoot':str(backup),'ProcessId':2147483646,'ProcessStartTicks':0,'Files':files}
 manifest=operation/'install.json';manifest.write_text(json.dumps(plan))
 run=subprocess.run([ps,'-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',str(helper),'-Manifest',str(manifest),'-NoRestart'],capture_output=True,text=True,timeout=25)
 result=json.loads((operation/'result.json').read_text(encoding='utf-8-sig'))
 if mode=='success':
  assert run.returncode==0 and result['State']=='installed', (run.stderr,result)
  assert all((target/n).read_bytes()==v for n,v in payload.items())
  assert (backup/'TVBox.exe').read_bytes()==original['TVBox.exe']
 else:
  assert run.returncode==1 and result['State']=='cancelled-or-restored',(run.stderr,result)
  assert all((target/n).read_bytes()==v for n,v in original.items())
  assert not (target/'libs/new.dll').exists()
 assert (target/'prefs.json').read_bytes()==original['prefs.json'] and (target/'history.json').read_bytes()==original['history.json']
 print('PASS helper',mode,'preserves user profile and',result['State'],flush=True)
for mode in ['success','rollback','hash','private']:scenario(mode,mode)
(base/'results.json').write_text(json.dumps({'scenarios':4,'passed':True}))
