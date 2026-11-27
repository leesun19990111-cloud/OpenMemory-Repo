// Read-only host code. No save, close, active-document changes, or preference changes.
var OpenMemoryReader = (function () {
    function xml(value) { return String(value).replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/>/g,"&gt;").replace(/"/g,"&quot;").replace(/'/g,"&apos;"); }
    function collect(host) {
        var rows = [], warnings = [], documents = [], appName = host;
        function add(path, name, dirty) {
            if (!path || !/^(?:[A-Za-z]:[\\/]|\\\\)/.test(path)) { warnings.push("저장된 로컬·공유폴더 경로 없음: " + name); return; }
            rows.push("<Entry><Path>"+xml(path)+"</Path><App>"+xml(appName)+"</App><Source>Adobe 문서 연결</Source><Note>"+xml(dirty ? "파일 복원 · 미저장 수정은 원본 앱에서 저장 필요" : "파일 복원")+"</Note></Entry>");
        }
        try {
            if (host === "PPRO") {
                appName = "Premiere Pro";
                if (!app.projects) throw new Error("여러 프로젝트 조회 API 없음");
                for (var p=0; p<app.projects.numProjects; p++) {
                    try { var project=app.projects[p]; add(project.path,project.name,false); } catch(e) { warnings.push("프로젝트 조회 실패: "+e); }
                }
            } else if (host === "AEFT") {
                appName = "After Effects";
                if (app.project) add(app.project.file ? app.project.file.fsName : "", "현재 After Effects 프로젝트", Boolean(app.project.dirty));
            } else {
                appName = host === "ILST" ? "Illustrator" : host === "IDSN" ? "InDesign" : "Photoshop";
                for (var i=0; i<app.documents.length; i++) {
                    try { var doc=app.documents[i]; var full=doc.fullName; add(full ? full.fsName || String(full) : "", doc.name, doc.saved === false || doc.modified === true); }
                    catch(e) { warnings.push("문서 경로 조회 실패 (미저장·클라우드 문서 포함): "+e); }
                }
            }
        } catch(e) { warnings.push("문서 연결 확인 필요: "+e); }
        var notes=""; for(var w=0; w<warnings.length; w++)notes+="<string>"+xml(warnings[w])+"</string>";
        return "<Files>"+rows.join("")+"</Files><Warnings>"+notes+"</Warnings>";
    }
    return {collect:collect};
}());
