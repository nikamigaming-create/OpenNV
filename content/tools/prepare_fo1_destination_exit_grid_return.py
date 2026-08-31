#!/usr/bin/env python3
"""Compile the exact VAULT13 exit-grid return to the already-bound V13ENT scene."""
from __future__ import annotations
import argparse, hashlib, json, os, re, tempfile
from pathlib import Path
from typing import Any
from fo1_profile import Fo1ProfileError, sha256_path

SCHEMA="opennv-fo1-exit-grid-transition/v1"
TRANSPORT="opennv-fo1-campaign-map-transport/v1"
PRESENTATION="opennv-fo1-campaign-presentation/v1"
MEDIC="opennv-fo1-destination-medic-look/v1"
MAP_SYMBOL=re.compile(r"^\s*#define\s+(MAP_[A-Z0-9_]+)\s+\(\s*(\d+)\s*\)",re.MULTILINE)

def read(p:Path)->dict[str,Any]: return json.loads(p.read_text(encoding='utf-8'))
def build(transport_path:Path,presentation_path:Path,forward_path:Path,medic_path:Path,maps_header:Path,output:Path)->dict[str,str]:
 if output.exists(): raise Fo1ProfileError(f"refusing to overwrite destination return exit-grid descriptor: {output}")
 transport,presentation,forward,medic=map(read,(transport_path,presentation_path,forward_path,medic_path))
 if transport.get('schema')!=TRANSPORT or presentation.get('schema')!=PRESENTATION or forward.get('schema')!=SCHEMA or medic.get('schema')!=MEDIC: raise Fo1ProfileError('unexpected return exit-grid prerequisite schema')
 source=transport['source']['map']; dest=forward['sourceMap']; current=forward['destination']
 if (medic['destination']['sourceMapSha256']!=source['sha256'] or current['name']!=source['file'] or
     current['mapSha256']!=source['sha256']): raise Fo1ProfileError('return exit-grid MAP identity drifted')
 values={name:int(value) for name,value in MAP_SYMBOL.findall(maps_header.read_text(encoding='cp1252'))}
 symbol=next((name for name,value in values.items() if value==dest['mapIndex']),None)
 if symbol is None: raise Fo1ProfileError('return exit-grid destination map index is absent from source maps header')
 reciprocal_values={tuple(row['instanceValues']) for row in forward.get('reciprocalTriggers',[])}
 if len(reciprocal_values)!=1: raise Fo1ProfileError('forward transition has no unique reciprocal exit-grid values')
 target=list(reciprocal_values.pop())
 if target[0]!=dest['mapIndex']: raise Fo1ProfileError('reciprocal exit-grid map index does not match V13ENT source')
 rows=[o for e in transport['objectGraph']['objects']['elevations'] for o in e['objects'] if o['prototype']['object_type']==5 and o['instanceValues']==target]
 if not rows: raise Fo1ProfileError('VAULT13 MAP has no exact reciprocal exit-grid values')
 doc={'schema':SCHEMA,'status':'compiled-owned-map-world-transition','inputs':{'transport':{'path':str(transport_path.resolve()),'sha256':sha256_path(transport_path)},'presentation':{'path':str(presentation_path.resolve()),'sha256':sha256_path(presentation_path)},'forwardTransition':{'path':str(forward_path.resolve()),'sha256':sha256_path(forward_path)},'medicLook':{'path':str(medic_path.resolve()),'sha256':sha256_path(medic_path)},'mapsHeader':{'path':str(maps_header.resolve()),'sha256':sha256_path(maps_header)}},'sourceMap':{'mapIndex':current['mapIndex'],'name':source['file'],'sha256':source['sha256']},'destination':{'mapIndex':dest['mapIndex'],'name':dest['name'],'mapSha256':dest['sha256'],'tile':target[1],'elevation':target[2],'rotation':target[3],'mapSymbol':symbol},'triggers':[{'serial':o['serial'],'tile':o['tile'],'pid':o['pid'],'prototypeSha256':o['prototype']['sha256'],'instanceValues':o['instanceValues']} for o in rows],'destinationScenePolicy':'existing-bound-source-scene-only'}
 data=(json.dumps(doc,indent=2,sort_keys=True)+'\n').encode();output.parent.mkdir(parents=True,exist_ok=True)
 with tempfile.NamedTemporaryFile(dir=output.parent,delete=False) as f: f.write(data);f.flush();os.fsync(f.fileno());temp=Path(f.name)
 os.replace(temp,output);return {'path':str(output.resolve()),'sha256':hashlib.sha256(data).hexdigest()}
def main()->int:
 p=argparse.ArgumentParser();
 for n in ('transport','presentation','forward-transition','medic-look','maps-header','output'):p.add_argument('--'+n,type=Path,required=True)
 a=p.parse_args()
 try: print('OPENNV_FO1_DESTINATION_RETURN_EXIT '+json.dumps(build(a.transport.resolve(),a.presentation.resolve(),a.forward_transition.resolve(),a.medic_look.resolve(),a.maps_header.resolve(),a.output.resolve()),sort_keys=True));return 0
 except Exception as e:print(f'OPENNV_FO1_DESTINATION_RETURN_EXIT_ERROR {e}');return 2
if __name__=='__main__':raise SystemExit(main())
