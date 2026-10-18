from pathlib import Path
import json,copy,struct,sys
root=json.loads(Path(__file__).with_name('template.json').read_text())
template=root['children'][1]['children'][0]
def node(name,a=None,children=None):return {'name':name,'a':a or {},'children':children or []}
def room(name,entities,attrs=None,spawn=(32,176)):
 r=copy.deepcopy(template);r['a'].update(name='lvl_'+name,x=0,y=0);r['a'].update(attrs or {})
 es=next(c for c in r['children'] if c['name']=='entities');es['children'][0]['a'].update(x=spawn[0],y=spawn[1]);es['children']+=entities
 return r
rooms=[room('ascend',[node('SummitBackgroundManager',{'id':1,'x':160,'y':160,'index':-1,'intro_launch':True,'cutscene':'','dark':False})],spawn=(160,176))]
# Rooms are disjoint; only explicit debug load selects them, no accidental overlap transitions.
for i,r in enumerate(rooms):r['a']['x']=i*640
root['children'][1]['children']=rooms
out=Path(sys.argv[1]);out.mkdir(parents=True,exist_ok=True)
(out/'everest.yaml').write_text('- Name: AkronQATransition\n  Version: 1.0.0\n  Dependencies:\n    - Name: Everest\n      Version: 1.0.0\n')
p=out/'Maps/AkronQA';p.mkdir(parents=True,exist_ok=True)
(p/'transition.meta.yaml').write_text('CoreMode: Hot\nModes:\n  - HeartIsEnd: true\n')
(p/'transition.json').write_text(json.dumps(root,indent=2))
keys=[]
def collect(n):
 for k in [n['name'],*n['a']]:
  if k not in keys:keys.append(k)
 for c in n['children']:collect(c)
collect(root)
f=(p/'transition.bin').open('wb')
def w(t,v):f.write(struct.pack('<'+t,v))
def string(s):
 b=s.encode();n=len(b)
 while n>=128:w('B',(n&127)|128);n>>=7
 w('B',n);f.write(b)
def emit(n):
 w('h',keys.index(n['name']));w('B',len(n['a']))
 for k,v in n['a'].items():
  w('h',keys.index(k))
  if isinstance(v,bool):w('B',0);w('?',v)
  elif isinstance(v,int):w('B',3);w('i',v)
  else:w('B',6);string(v)
 w('h',len(n['children']))
 for c in n['children']:emit(c)
string('CELESTE MAP');string('transition');w('h',len(keys))
for k in keys:string(k)
emit(root);f.close()
print(p/'transition.bin')
