internal static class ClassicInventoryPage
{
    internal static string Render(string data) => """
        <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>OpenNV · Classic asset inventory</title>
        <style>
        :root{color-scheme:dark;font:16px system-ui;background:#131816;color:#dae3d9}body{max-width:1500px;margin:auto;padding:32px}
        h1{font-size:34px;margin:0 0 10px}p{line-height:1.6;max-width:1050px;color:#bdcbbd}.note{border-left:3px solid #dfa95c;padding-left:18px}
        .stats{display:flex;gap:16px;flex-wrap:wrap;margin:24px 0}.stat{background:#222b24;padding:18px;border-radius:8px;min-width:190px}
        .stat strong{display:block;font-size:26px;color:#c5efad}input,select,button{font:inherit;padding:10px;background:#202820;color:#e0e9da;border:1px solid #536552;border-radius:6px}
        nav{display:flex;flex-wrap:wrap;gap:12px;position:sticky;top:0;background:#131816;padding:12px 0;z-index:2}input{flex:1;min-width:260px}
        table{width:100%;border-collapse:collapse}th{text-align:left;color:#9fb697;padding:12px}td{padding:12px;border-top:1px solid #354334;vertical-align:top}
        td img{width:110px;height:100px;object-fit:contain;image-rendering:pixelated;background:#1d211c}small{display:block;color:#a7b29f;margin-top:6px}
        .gap{color:#ffb99a}.candidate{color:#e5d58e}details{max-width:500px}summary{cursor:pointer}ul{padding-left:20px;font-size:14px;line-height:1.6}a{color:#b6e39a}
        #count{padding:12px 0;color:#adc89e}footer{padding:25px 0}.empty{padding:50px;text-align:center}
        </style>
        <h1>Classic Fallout · Asset inventory</h1>
        <p>Fallout 1 and Fallout 2, from their selected owned files. Source artwork is shown here as a modeling reference.
        A world sprite is an unfinished 3D replacement; original interface art and illustrated portraits remain intentional 2D.
        A declared model is a candidate until its materials, pose, scale, orientation,
        placement and animation work in the game.</p>
        <p class="note">Private local report. No extracted art is a runtime input or a distributable asset.
        The inventory covers stored maps, prototypes and nested item inventories. It does not prove script-spawned states,
        campaign execution or visual parity. Source read failures remain listed below.</p>
        <div id="stats" class="stats"></div>
        <nav><input id="search" placeholder="Find an asset, model, map or prototype…" aria-label="Search inventory">
        <select id="game" aria-label="Campaign"><option value="">Both games</option><option>fallout-1</option><option>fallout-2</option></select>
        <select id="category" aria-label="Category"><option value="">Every category</option></select>
        <select id="route" aria-label="Presentation"><option value="">Every presentation status</option></select>
        <select id="usage" aria-label="Usage"><option value="visible">Visible world placements</option><option value="used">Maps and inventories</option><option value="all">All available art</option></select></nav>
        <div id="count"></div><table><thead><tr><th>Source reference</th><th>Identity / source files</th><th>3D replacement</th><th>Where used</th></tr></thead><tbody id="rows"></tbody></table>
        <footer><button id="more">Show next 100</button><p><a href="inventory.json">Full inventory JSON</a> · <a href="placements.jsonl">Exact source placements and tile indices</a> · <a href="donor-catalog.json">Owned FNV / FO3 resource catalogs</a></p>
        <details id="failures"><summary>Source read failures</summary></details></footer>
        <script id="data" type="application/json">
        """ + data + """
        </script><script>
        const data=JSON.parse(document.querySelector('#data').textContent), $=id=>document.getElementById(id);
        const escape=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
        const label=s=>s.replaceAll('-',' ');let limit=100;
        $('stats').innerHTML=data.campaigns.map(c=>`<div class="stat">${escape(c.campaign)}<strong>${c.decodedMaps} / ${c.sourceMaps} maps</strong>${c.elevations} elevations · ${c.artFiles.toLocaleString()} art files<br>${c.assetFamilies.toLocaleString()} asset families<br>${c.worldObjects.toLocaleString()} world objects · ${c.nestedInventoryEntries.toLocaleString()} inventory entries</div>`).join('')+
          data.donors.map(d=>`<div class="stat">${escape(d.game)}<strong>${d.modelsAndAnimations.toLocaleString()}</strong>mesh / animation resources<br>${d.textures.toLocaleString()} textures</div>`).join('');
        for(const [id,key] of [['category','Category'],['route','Route']])for(const value of [...new Set(data.assets.map(a=>a[key]))].sort())$(id).add(new Option(label(value),value));
        $('failures').querySelector('summary').textContent=`${data.failures.length} unresolved source rows`;
        const pre=document.createElement('pre');pre.style.whiteSpace='pre-wrap';pre.textContent=JSON.stringify(data.failures,null,2);$('failures').append(pre);
        function render(){const query=$('search').value.toLowerCase(),usage=$('usage').value;
          const results=data.assets.filter(a=>(!$('game').value||a.Campaign===$('game').value)&&(!$('category').value||a.Category===$('category').value)&&
            (!$('route').value||a.Route===$('route').value)&&(usage==='all'||usage==='visible'&&a.VisiblePlacements>0||usage==='used'&&(a.WorldPlacements+a.InventoryEntries+a.FloorTiles+a.RoofTiles)>0)&&
            (!query||[a.Key,a.Model,...a.Prototypes,...Object.keys(a.Maps)].join(' ').toLowerCase().includes(query)));
          $('count').textContent=`${results.length.toLocaleString()} matching families · ${results.reduce((s,a)=>s+a.VisiblePlacements,0).toLocaleString()} visible source placements · no acceptance implied`;
          $('rows').innerHTML=results.slice(0,limit).map(a=>`<tr><td>${a.Reference?`<img loading="lazy" src="${escape(a.Reference)}" alt="${escape(a.Key)}">`:''}</td><td><strong>${escape(a.Key)}</strong><small>${escape(a.Campaign)} · ${a.Width} × ${a.Height} · ${a.SourceAnimationFiles} source files</small><details><summary>Source identities</summary><ul>${a.Prototypes.map(p=>`<li>PID ${escape(p)}</li>`).join('')}${a.Files.map(p=>`<li>${escape(p)}</li>`).join('')}</ul></details></td>
          <td><strong class="${a.Route==='missing-3d'?'gap':'candidate'}">${escape(label(a.Route))}</strong><small>${escape(a.Model)}</small><p>${escape(a.RequiredWork)}</p>${a.Errors.map(e=>`<small class="gap">${escape(e)}</small>`).join('')}</td>
          <td>${a.VisiblePlacements} visible world placements<small>${a.WorldPlacements} stored world objects · ${a.InventoryEntries} inventory entries (${a.InventoryQuantity} items)<br>${a.FloorTiles} floor / ${a.RoofTiles} roof tiles</small><details><summary>${Object.keys(a.Maps).length} map / elevation uses</summary><ul>${Object.entries(a.Maps).map(([m,n])=>`<li>${escape(m)} — ${n}</li>`).join('')}</ul></details></td></tr>`).join('')||'<tr><td class="empty" colspan="4">No matching source assets.</td></tr>';
          $('more').hidden=results.length<=limit;
        }
        for(const id of ['search','game','category','route','usage'])$(id).addEventListener(id==='search'?'input':'change',()=>{limit=100;render()});
        $('more').onclick=()=>{limit+=100;render()};render();
        </script></html>
        """;
}
