// Load JSZip from CDN first
const script = document.createElement('script');
script.src = 'https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js';
document.head.appendChild(script);

script.onload = async () => {
  const zip = new JSZip();
  const items = document.querySelectorAll('div.sheet-image');
  
  for (let i = 0; i < items.length; i++) {
    const img = items[i].style.backgroundImage.slice(4, -1).replace(/"/g, "");
    // div => <div class="sheet-image" style="background-image: url(&quot;https://cdn.flowkey.com/songs/xxx/xxx.png&quot;); width: 207px; left: 32256px; top: -30px;"></div>
    try {
      const response = await fetch(img);
      const blob = await response.blob();
      zip.file(`image_${i}.png`, blob);
      console.log(`Added image ${i} to zip`);
    } catch (err) {
      console.error(`Failed to fetch image ${i}:`, err);
    }
  }

  var videoLink = document.querySelector('video.player-video').src;
  var nameStart = videoLink.lastIndexOf('/') + 1;
  var nameEnd = videoLink.indexOf('_');
  let fileName = videoLink.substring(nameStart, nameEnd);
  
  const zipBlob = await zip.generateAsync({ type: 'blob' });
  const url = URL.createObjectURL(zipBlob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName + '.zip';
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
};
