using System.Net;
using System.Text.Json;

namespace OpenNV.AnimationCompare;

internal static class ComparisonHtml
{
    internal static void Write(string path, PoseComparisonReport report, string? retailVideo, string? openNvVideo)
    {
        static string Video(string? value, string label)
        {
            if (value is null) return $"<div class=absent>{label} video was not supplied</div>";
            if (!File.Exists(value)) throw new FileNotFoundException("Selected comparison video is absent.", value);
            return $"<video controls preload=metadata src=\"{WebUtility.HtmlEncode(new Uri(Path.GetFullPath(value)).AbsoluteUri)}\"></video>";
        }
        var data = JsonSerializer.Serialize(report, Program.Json);
        var html = """
<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>OpenNV motion comparison</title><style>
:root{color-scheme:dark;font:15px system-ui;background:#101418;color:#e4eaf0}body{margin:0;padding:24px;max-width:1800px;margin:auto}
h1{font-size:26px;margin:0 0 8px}.note{color:#abbac8;max-width:1100px;line-height:1.5}.panels{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin:22px 0}video{width:100%;background:#000}.absent{background:#1a222b;min-height:100px;display:grid;place-items:center;color:#8293a3}.label{font-size:13px;color:#94a9bb;margin-bottom:7px}
button,input,select{font:inherit;padding:8px 12px;border:1px solid #40505e;border-radius:5px;background:#202b35;color:inherit}input[type=search]{width:290px}button{cursor:pointer}.controls{display:flex;gap:14px;align-items:center;flex-wrap:wrap}.cards{display:flex;gap:12px;flex-wrap:wrap;margin:20px 0}.card{background:#1a242e;padding:12px 18px;border-radius:6px}.card strong{display:block;font-size:24px}table{border-collapse:collapse;width:100%;margin-top:15px;font-variant-numeric:tabular-nums}th{text-align:left;font-size:12px;color:#9fb0bf;position:sticky;top:0;background:#16212a}td,th{padding:10px;border-bottom:1px solid #2c3945}tr.issue{background:#452623}tr.unjoined{background:#302e22}.small{font-size:12px;color:#aebdca}.findings{max-width:440px}details{padding:12px 0}summary{cursor:pointer}pre{white-space:pre-wrap;word-break:break-word}.bar{width:170px;background:#273847;height:8px;margin:4px 0}.bar span{height:8px;display:block;background:#76c9d1}.bar.ours span{background:#f4b562}
</style><h1>Motion comparison</h1><div class=note>Recorded motion, phase joins and unresolved differences. This report does not certify rendering or gameplay parity. Numeric thresholds are diagnostic controls.</div>
<div class=panels><section><div class=label>RETAIL</div>@@RETAIL@@</section><section><div class=label>OPENNV</div>@@OPENNV@@</section></div>
<div class=controls><button id=play>Play both videos</button><button id=pause>Pause both</button><label>Video offset, seconds <input id=offset type=number value=0 step=.01 style="width:90px"></label><span class=small>Video timing is not a bone-phase join.</span></div>
<div class=cards id=cards></div><div class=controls><select id=actor></select><input id=search type=search placeholder="Find a bone or finding"><label><input id=moving type=checkbox> Moving bones only</label><label><input id=issues type=checkbox> Investigation rows only</label></div>
<section style="margin:22px 0"><h2>Observed animation resources</h2><div class=note>Active sequence and explicit pose-owner resource observations. Counts are samples, not blend contributions; slot -1 and owner-only evidence have no qualified slot join. A resource unobserved in this take is not automatically unimplemented.</div><table><thead><tr><th>Resource</th><th>Retail samples / total<br>slot : state · samples</th><th>OpenNV samples / total<br>slot : state · samples</th><th>Observation</th></tr></thead><tbody id=resources></tbody></table></section>
<div id=actorNote class=note style="margin-top:14px"></div><table><thead><tr><th>Bone</th><th>Motion speed (°/s)<br>retail / OpenNV</th><th>Largest step (°)<br>retail / OpenNV</th><th>Layer-switch step (°)</th><th>Phase-joined error<br>angle / position</th><th>Findings</th></tr></thead><tbody id=rows></tbody></table>
<details><summary>Input and comparison boundaries</summary><pre id=boundary></pre></details><script id=data type=application/json>@@DATA@@</script><script>
const data=JSON.parse(document.getElementById('data').textContent),$=id=>document.getElementById(id),esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])),fmt=(n,d=2)=>n==null?'unjoined':Number(n).toFixed(d);
data.actors.forEach((a,i)=>$('actor').add(new Option(a.reference,i)));const bones=data.actors.flatMap(a=>a.bones),cards=[['Observed bones',bones.length],['Moving locally',bones.filter(b=>b.openNvMotion.moving||b.retailMotion?.moving).length],['Investigate',bones.filter(b=>b.status==='investigate').length],['Reference-only resources',data.actors.reduce((n,a)=>n+a.animationResources.filter(r=>r.status==='unobserved-in-candidate-take').length,0)],['Phase-joined samples',data.actors.reduce((n,a)=>n+a.phaseJoinedSamples,0)]];$('cards').innerHTML=cards.map(([k,v])=>`<div class=card><strong>${v}</strong>${k}</div>`).join('');
function resourceObservation(o){return `<strong>${o.samples} / ${o.totalSamples}</strong><div class=small>${o.slotStates.map(s=>`${s.slot} : ${s.state} / ${s.samples}`).concat((o.unqualifiedOwnerStates||[]).map(s=>`owner ${esc(s.owner)} : ${s.state??'state unobserved'} / ${s.samples} (slot/contribution unqualified)`)).join('<br>')||'unobserved'}<br>Qualified layer coverage: ${o.qualifiedLayerSamples} / ${o.samples} observed samples</div>`}
function render(){
 const a=data.actors[+$('actor').value];if(!a)return;const q=$('search').value.toLowerCase();
 $('resources').innerHTML=a.animationResources.map(r=>`<tr class="${r.status==='unobserved-in-candidate-take'?'issue':r.status==='unobserved-in-reference-take'?'unjoined':''}"><td>${esc(r.resource||'(resource identity unobserved)')}</td><td>${resourceObservation(r.retail)}</td><td>${resourceObservation(r.openNv)}</td><td>${esc(r.status.replaceAll('-',' '))}</td></tr>`).join('');
 $('actorNote').textContent=`Body travel: OpenNV ${fmt(a.openNvTravel.travelMeters)} m, ${a.openNvTravel.backwardIntervals} backwards intervals; retail ${fmt(a.retailTravel?.travelMeters)} m, ${a.retailTravel?.backwardIntervals??'unobserved'} backwards intervals. Findings: ${a.findings.join('; ')}. Unjoined phases: ${JSON.stringify(a.unjoinedPhaseReasons)}.`;
 const list=a.bones.filter(b=>(!$('moving').checked||b.openNvMotion.moving||b.retailMotion?.moving)&&(!$('issues').checked||b.status==='investigate')&&`${b.bone} ${b.findings.join(' ')}`.toLowerCase().includes(q));
 $('rows').innerHTML=list.map(b=>{const r=b.retailMotion,o=b.openNvMotion,p=o.peak;return `<tr class="${b.status==='investigate'?'issue':b.status==='unjoined'?'unjoined':''}"><td>${esc(b.bone)}<div class=small>${esc(b.status)} · ${b.phaseJoinedSamples} joined poses${p?` · largest step at ${fmt(p.seconds,3)} s / frame ${p.frame}`:''}</div></td><td>Retail ${fmt(r?.maximumDegreesPerSecond)}<div class=small>${esc(r?.rateTimeBasis??'unobserved')}</div>OpenNV ${fmt(o.maximumDegreesPerSecond)}<div class=small>${esc(o.rateTimeBasis)}${o.simulationRateQualified?' · qualified simulation rate':' · simulation rate unqualified'}</div><div class=small>Rate ratio: ${fmt(b.angularSpeedRatio)}</div></td><td>${fmt(r?.maximumStepDegrees)} / ${fmt(o.maximumStepDegrees)}</td><td>${fmt(o.maximumTransitionDegrees)}<div class=small>within layer ${fmt(o.maximumWithinLayerDegrees)}</div></td><td>${fmt(b.maximumRotationDifferenceDegrees)}° / ${b.maximumPositionDifferenceMeters==null?'unjoined':fmt(b.maximumPositionDifferenceMeters*1000)+' mm'}</td><td class="findings small">${b.findings.map(esc).join('<br>')}</td></tr>`}).join('')
}
['actor','search','moving','issues'].forEach(id=>$(id).addEventListener('input',render));$('boundary').textContent=JSON.stringify({boundary:data.boundary,inputProblems:data.inputProblems,options:data.options,retailInput:data.retailInput,openNvInput:data.openNvInput},null,2);const videos=[...document.querySelectorAll('video')];$('play').onclick=()=>{if(videos.length===2)videos[1].currentTime=Math.max(0,videos[0].currentTime+(+$('offset').value||0));videos.forEach(v=>v.play().catch(()=>{}))};$('pause').onclick=()=>videos.forEach(v=>v.pause());render();
</script></html>
""";
        html = html.Replace("@@RETAIL@@", Video(retailVideo, "Retail"), StringComparison.Ordinal)
            .Replace("@@OPENNV@@", Video(openNvVideo, "OpenNV"), StringComparison.Ordinal)
            .Replace("@@DATA@@", data, StringComparison.Ordinal);
        File.WriteAllText(path, html);
    }
}
