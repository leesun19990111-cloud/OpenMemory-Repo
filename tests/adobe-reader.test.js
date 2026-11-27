const fs=require('fs'), vm=require('vm'), assert=require('assert');
const root=require('path').resolve(__dirname,'..');
const script=fs.readFileSync(require('path').join(root,'adobe','reader.jsx'),'utf8');
let checks=[];
function collect(app,host){let context={app};vm.createContext(context);vm.runInContext(script,context);return context.OpenMemoryReader.collect(host);}
function check(ok,name){assert(ok,name);checks.push('PASS: '+name);}
let result=collect({projects:{numProjects:2,0:{name:'A',path:'C:\\작업\\A.prproj'},1:{name:'B',path:'D:\\작업\\B & C.prproj'}}},'PPRO');
check((result.match(/<Entry>/g)||[]).length===2 && result.includes('B &amp; C.prproj'),'Premiere enumerates every open project and XML escapes paths');
result=collect({project:{file:{fsName:'C:\\작업\\A.aep'},dirty:true}},'AEFT');
check(result.includes('A.aep') && result.includes('미저장'),'After Effects captures saved project and unsaved-change warning');
result=collect({project:{file:null,dirty:true}},'AEFT');
check(!result.includes('<Entry>') && result.includes('경로 없음'),'Unsaved After Effects project becomes a warning without invented path');
for(const host of ['PHXS','ILST','IDSN']){
 result=collect({documents:[{fullName:{fsName:'C:\\작업\\A.psd'},name:'A',saved:false},{fullName:{fsName:'C:\\작업\\B.psd'},name:'B',saved:true}]},host);
 check((result.match(/<Entry>/g)||[]).length===2,host+' captures all documents, not only the active document');
}
result=collect({documents:[{get fullName(){throw Error('unsaved');},name:'Untitled'},{fullName:{fsName:'C:\\B.ai'},name:'B'}]},'ILST');
check(result.includes('C:\\B.ai') && result.includes('조회 실패'),'One unsaved document does not prevent other documents being captured');
result=collect({project:{file:{fsName:'https://cloud/project.aep'}}},'AEFT');
check(!result.includes('<Entry>'),'Cloud URL is not misidentified as a local project');
console.log(checks.join('\n'));
module.exports=checks;
