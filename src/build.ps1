# Monta o site (pasta site/) a partir de orbita.html, gera os ícones e compila windows/Orbita.exe.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$site = Join-Path $root 'site'
$win = Join-Path $root 'windows'
New-Item -ItemType Directory -Force $site, (Join-Path $site 'icons') | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)

# --- index.html: o trecho até </style> vai para o <head>, o resto para o <body>
$src = [IO.File]::ReadAllText((Join-Path $root 'orbita.html'), $utf8)
$cut = $src.IndexOf('</style>') + 8
$head = $src.Substring(0, $cut)
$body = $src.Substring($cut)
$html = @"
<!doctype html>
<html lang="pt-BR">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="description" content="Órbita: tarefas e rotinas em círculos. O centro é o agora, o resto pode esperar.">
<meta name="theme-color" content="#0E151C">
<meta name="mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-title" content="Órbita">
<link rel="manifest" href="manifest.webmanifest">
<link rel="icon" href="icons/icon-192.png">
<link rel="apple-touch-icon" href="icons/icon-192.png">
<style>:root{padding-top:env(safe-area-inset-top,0px);padding-bottom:env(safe-area-inset-bottom,0px)}body{margin:0}[hidden]{display:none!important}</style>
$head
</head>
<body>
$body
</body>
</html>
"@
[IO.File]::WriteAllText((Join-Path $site 'index.html'), $html, $utf8)

$manifest = @'
{
  "name": "Órbita",
  "short_name": "Órbita",
  "description": "Tarefas e rotinas em círculos: o centro é o agora, o resto pode esperar.",
  "lang": "pt-BR",
  "start_url": "./index.html",
  "scope": "./",
  "display": "standalone",
  "orientation": "any",
  "background_color": "#0E151C",
  "theme_color": "#0E151C",
  "icons": [
    { "src": "icons/icon-192.png", "sizes": "192x192", "type": "image/png", "purpose": "any" },
    { "src": "icons/icon-512.png", "sizes": "512x512", "type": "image/png", "purpose": "any" },
    { "src": "icons/maskable-512.png", "sizes": "512x512", "type": "image/png", "purpose": "maskable" }
  ]
}
'@
[IO.File]::WriteAllText((Join-Path $site 'manifest.webmanifest'), $manifest, $utf8)

$sw = @'
/* Órbita — service worker: rede primeiro, cache como reserva para uso offline */
const CACHE = 'orbita-v1';
const FILES = ['./', 'index.html', 'manifest.webmanifest', 'icons/icon-192.png', 'icons/icon-512.png'];

self.addEventListener('install', e => e.waitUntil(caches.open(CACHE).then(c => c.addAll(FILES)).then(() => self.skipWaiting())));
self.addEventListener('activate', e => e.waitUntil(caches.keys().then(ks => Promise.all(ks.filter(k => k !== CACHE).map(k => caches.delete(k)))).then(() => self.clients.claim())));
self.addEventListener('fetch', e => {
  const u = new URL(e.request.url);
  if (e.request.method !== 'GET' || u.origin !== location.origin || u.pathname.includes('/api/')) return;
  e.respondWith(fetch(e.request).then(r => {
    if (r.ok) { const copy = r.clone(); caches.open(CACHE).then(c => c.put(e.request, copy)); }
    return r;
  }).catch(() => caches.match(e.request, { ignoreSearch: true }).then(r => r || caches.match('index.html'))));
});
'@
[IO.File]::WriteAllText((Join-Path $site 'sw.js'), $sw, $utf8)
[IO.File]::WriteAllText((Join-Path $site '.nojekyll'), '', $utf8)

# --- ícones: o mesmo desenho da logo (núcleo e três órbitas com um ponto em cada)
Add-Type -AssemblyName System.Drawing
function New-Icon([int]$size, [double]$scale, [bool]$round) {
  $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
  $g.Clear([System.Drawing.Color]::Transparent)
  $bg = [System.Drawing.ColorTranslator]::FromHtml('#0E151C')
  $bb = New-Object System.Drawing.SolidBrush($bg)
  if ($round) {
    $r = [single]($size * 0.22); $d = 2 * $r; $w = [single]$size
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc(0, 0, $d, $d, 180, 90); $p.AddArc($w - $d, 0, $d, $d, 270, 90)
    $p.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $p.AddArc(0, $w - $d, $d, $d, 90, 90); $p.CloseFigure()
    $g.FillPath($bb, $p)
  } else { $g.FillRectangle($bb, 0, 0, $size, $size) }
  $c = $size / 2.0; $u = $size / 64.0 * $scale
  # raio, espessura, cor, ângulo do ponto (graus), raio do ponto
  $rings = @(@(26.5, 2.2, '#8CA4E6', -40, 4.4), @(18.6, 2.8, '#4CC2B1', 150, 3.8), @(10.8, 3.3, '#F2B552', 45, 3.1))
  foreach ($rg in $rings) {
    $rad = $rg[0] * $u; $col = [System.Drawing.ColorTranslator]::FromHtml($rg[2])
    $pen = New-Object System.Drawing.Pen($col, [single]($rg[1] * $u))
    $g.DrawEllipse($pen, [single]($c - $rad), [single]($c - $rad), [single](2 * $rad), [single](2 * $rad))
    $a = $rg[3] * [Math]::PI / 180; $dx = $c + $rad * [Math]::Cos($a); $dy = $c + $rad * [Math]::Sin($a)
    $dr = ($rg[4] + 1.6) * $u
    $g.FillEllipse($bb, [single]($dx - $dr), [single]($dy - $dr), [single](2 * $dr), [single](2 * $dr))
    $dr = $rg[4] * $u
    $g.FillEllipse((New-Object System.Drawing.SolidBrush($col)), [single]($dx - $dr), [single]($dy - $dr), [single](2 * $dr), [single](2 * $dr))
  }
  $cr = 5.2 * $u
  $g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#FF7A4F'))), [single]($c - $cr), [single]($c - $cr), [single](2 * $cr), [single](2 * $cr))
  $g.Dispose()
  return $bmp
}
function Save-Png($bmp, $path) { $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose() }
Save-Png (New-Icon 192 1.0 $true) (Join-Path $site 'icons\icon-192.png')
Save-Png (New-Icon 512 1.0 $true) (Join-Path $site 'icons\icon-512.png')
Save-Png (New-Icon 512 0.72 $false) (Join-Path $site 'icons\maskable-512.png')

# --- .ico com várias medidas (entradas em PNG)
$sizes = 16, 32, 48, 256
$pngs = foreach ($s in $sizes) { $ms = New-Object IO.MemoryStream; $b = New-Icon $s 1.0 $true; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); , $ms.ToArray() }
$ico = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter($ico)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $bw.Write([byte]($sizes[$i] % 256)); $bw.Write([byte]($sizes[$i] % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush(); [IO.File]::WriteAllBytes((Join-Path $win 'orbita.ico'), $ico.ToArray())

# --- Orbita.exe
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe /optimize+ /codepage:65001 "/out:$win\Orbita.exe" "/win32icon:$win\orbita.ico" `
  "/resource:$site\index.html,index.html" "/resource:$site\manifest.webmanifest,manifest.webmanifest" `
  "/resource:$site\icons\icon-192.png,icon-192.png" "/resource:$site\icons\icon-512.png,icon-512.png" `
  "/resource:$site\icons\maskable-512.png,maskable-512.png" `
  /r:System.Windows.Forms.dll "$win\Orbita.cs"
if ($LASTEXITCODE -ne 0) { throw 'csc falhou' }
Get-ChildItem $site -Recurse -File | Select-Object @{n='arquivo';e={$_.FullName.Substring($root.Length + 1)}}, Length
Get-Item "$win\Orbita.exe" | Select-Object Name, Length
