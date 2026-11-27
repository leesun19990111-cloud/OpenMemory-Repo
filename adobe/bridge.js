/* Local-only CEP collector. Never receives executable code or file-open requests. */
(function () {
    "use strict";
    try {
    var req = typeof require === "function" ? require : cep_node.require;
    var fs=req("fs"), path=req("path"), proc=req("process"), crypto=req("crypto");
    var host=JSON.parse(window.__adobe_cep__.getHostEnvironment());
    var instance=crypto.randomBytes(16).toString("hex");
    var root=path.join(proc.env.LOCALAPPDATA,"OpenMemory","adobe");
    var extension=decodeURI(window.__adobe_cep__.getSystemPath("extension")).replace(/^file:\/\/\//,"");
    var reader=fs.readFileSync(path.join(extension,"reader.jsx"),"utf8");
    var state=path.join(root,"state-"+instance+".xml");
    var busy=false;
    function xml(v) {return String(v).replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/>/g,"&gt;").replace(/"/g,"&quot;");}
    function mkdir(p) {if(!fs.existsSync(p)){mkdir(path.dirname(p));try{fs.mkdirSync(p);}catch(e){if(!fs.existsSync(p))throw e;}}}
    function tick() {
        if(busy)return;
        busy=true;
        window.__adobe_cep__.evalScript("(function(){\n"+reader+"\nreturn OpenMemoryReader.collect("+JSON.stringify(host.appId)+");}())",function(result) {
            try {
                if(result.indexOf("<Files>")!==0)result="<Files></Files><Warnings><string>문서 조회 응답 실패: "+xml(result)+"</string></Warnings>";
                mkdir(root);
                var data='<?xml version="1.0" encoding="utf-8"?><AdobeState><CepPid>'+proc.pid+'</CepPid><Instance>'+instance+'</Instance><Host>'+xml(host.appId)+'</Host><Version>'+xml(host.appVersion)+'</Version><Updated>'+new Date().toISOString()+'</Updated>'+result+'</AdobeState>';
                var temporary=state+".tmp";
                fs.writeFileSync(temporary,data,"utf8");fs.renameSync(temporary,state);
            } catch(e) { /* Do not interrupt editing for helper I/O errors. */ }
            finally {busy=false;}
        });
    }
    tick();setInterval(tick,3000);
    window.addEventListener("unload",function(){try{fs.unlinkSync(state);}catch(e){};});
    } catch(error) {
        console.error("OpenMemory connection failed: "+error);
        try {var diagnostic=decodeURI(window.__adobe_cep__.getSystemPath("userData")).replace(/^file:\/\/\//,"")+"/OpenMemory-adobe-diagnostic.txt";window.cep.fs.writeFile(diagnostic,String(error));}catch(ignore){}
    }
}());
