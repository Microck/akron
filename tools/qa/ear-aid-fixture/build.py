from pathlib import Path
import json,copy,struct,sys
root=json.loads(Path(__file__).with_name('template.json').read_text())
template=root['children'][1]['children'][0]
def node(name,a=None,children=None):return {'name':name,'a':a or {},'children':children or []}
def room(name,entities,attrs=None,spawn=(32,176)):
 r=copy.deepcopy(template);r['a'].update(name='lvl_'+name,x=0,y=0);r['a'].update(attrs or {})
 es=next(c for c in r['children'] if c['name']=='entities');es['children'][0]['a'].update(x=spawn[0],y=spawn[1]);es['children']+=entities
 return r
rooms=[
 room('fire',[node('fireBall',{'id':1,'x':160,'y':120,'amount':1,'offset':0,'speed':1,'notCoreMode':False},[node('node',{'x':240,'y':120})])]),
 room('lava',[node('fireBarrier',{'id':2,'x':176,'y':112,'width':40,'height':64})]),
 room('move',[node('moveBlock',{'id':3,'x':96,'y':152,'width':32,'height':24,'direction':'Right','canSteer':True,'fast':False})],spawn=(80,176)),
 room('fuse',[node('lightningBlock',{'id':4,'x':120,'y':144,'flipX':False,'flag':False,'music':'','music_progress':-1,'music_session':False})],spawn=(80,176)),
 room('wind',[],{'windPattern':'LeftStrong','ambience':'event:/new_content/env/10_voidspiral'}),
 room('core',[node('bounceBlock',{'id':5,'x':128,'y':152,'width':32,'height':24,'notCoreMode':False})],spawn=(112,176)),
]
rooms.append(room('unlock',[node('blackGem',{'id':6,'x':88,'y':152,'fake':False})]))
rooms.append(room('golden',[node('goldenBerry',{'id':7,'x':32,'y':144,'winged':False})]))
r=room('seeker',[node('seeker',{'id':8,'x':160,'y':152}),node('fallingBlock',{'id':9,'x':128,'y':80,'width':64,'height':32,'tiletype':'3','behind':False,'climbFall':True})],spawn=(152,80))
solids=next(c for c in r['children'] if c['name']=='solids');grid=[list(row) for row in solids['a']['innerText'].split('\n')]
for y in range(8,22):
 grid[y][15]='1';grid[y][24]='1'
solids['a']['innerText']='\n'.join(''.join(row) for row in grid);rooms.append(r)
# Rooms are disjoint; only explicit debug load selects them, no accidental overlap transitions.
for i,r in enumerate(rooms):r['a']['x']=i*640
root['children'][1]['children']=rooms
out=Path(sys.argv[1]);out.mkdir(parents=True,exist_ok=True)
(out/'everest.yaml').write_text('- Name: AkronQASounds\n  Version: 1.0.0\n  Dependencies:\n    - Name: Everest\n      Version: 1.0.0\n')
p=out/'Maps/AkronQA';p.mkdir(parents=True,exist_ok=True)
(p/'sounds.meta.yaml').write_text('CoreMode: Hot\nModes:\n  - HeartIsEnd: true\n')
(p/'sounds.json').write_text(json.dumps(root,indent=2))
keys=[]
def collect(n):
 for k in [n['name'],*n['a']]:
  if k not in keys:keys.append(k)
 for c in n['children']:collect(c)
collect(root)
f=(p/'sounds.bin').open('wb')
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
string('CELESTE MAP');string('sounds');w('h',len(keys))
for k in keys:string(k)
emit(root);f.close()
print(p/'sounds.bin')
