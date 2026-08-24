let stream, timer, busy = false;
const interval = 400, width = 640;
export async function start(video, canvas, token, dotnet) {
  if (!window.isSecureContext) return 'insecure';
  if (!navigator.mediaDevices?.getUserMedia) return 'unsupported';
  try { stream = await navigator.mediaDevices.getUserMedia({video:{facingMode:'environment',width:{ideal:width}},audio:false}); }
  catch(e) { return ['NotAllowedError','SecurityError'].includes(e.name)?'denied':e.name==='NotFoundError'?'nocamera':e.name==='NotReadableError'?'inuse':'failed'; }
  video.srcObject=stream; await video.play(); timer=setInterval(()=>grab(video,canvas,token,dotnet),interval); return 'ok';
}
async function grab(video,canvas,token,dotnet){
  if(busy||!video.videoWidth)return; busy=true;
  try{const scale=width/video.videoWidth;canvas.width=width;canvas.height=Math.round(video.videoHeight*scale);canvas.getContext('2d').drawImage(video,0,0,canvas.width,canvas.height);const blob=await new Promise(r=>canvas.toBlob(r,'image/jpeg',.6));if(!blob)return;const response=await fetch(`/hr/kiosk/${encodeURIComponent(token)}/frame`,{method:'POST',headers:{'Content-Type':'image/jpeg'},body:blob});if(response.status===200){const {text}=await response.json();if(text)await dotnet.invokeMethodAsync('OnCameraScan',text);}}catch{}finally{busy=false;}
}
export function stop(video){if(timer)clearInterval(timer);timer=null;if(stream)stream.getTracks().forEach(t=>t.stop());stream=null;if(video)video.srcObject=null;}
