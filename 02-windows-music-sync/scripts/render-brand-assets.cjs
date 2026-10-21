/* Design-time only. Runtime and MSIX builds do not depend on Node or sharp.
   Install sharp separately, or set MUSIC_SYNC_SHARP_MODULE_DIR to an existing
   node_modules directory. Run: node scripts/render-brand-assets.cjs */
'use strict';
const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');
let sharp;
try { sharp = require('sharp'); }
catch {
  if (!process.env.MUSIC_SYNC_SHARP_MODULE_DIR) {
    throw new Error('Design export needs sharp. Set MUSIC_SYNC_SHARP_MODULE_DIR to an existing node_modules directory.');
  }
  sharp = require(require.resolve('sharp', { paths: [process.env.MUSIC_SYNC_SHARP_MODULE_DIR] }));
}

async function main() {
  const root = path.resolve(__dirname, '..');
  const branding = path.join(root, 'Packaging', 'Branding');
  const assets = path.join(root, 'Packaging', 'Assets');
  const store = path.join(root, 'Packaging', 'StoreListing');
  await fs.mkdir(assets, { recursive: true });
  await fs.mkdir(store, { recursive: true });
  const icon = await fs.readFile(path.join(branding, 'music-sync-icon.svg'), 'utf8');
  const mark = await fs.readFile(path.join(branding, 'music-sync-mark.svg'), 'utf8');
  const light = await fs.readFile(path.join(branding, 'music-sync-mark-light.svg'), 'utf8');
  const wide = `<svg xmlns="http://www.w3.org/2000/svg" width="2116" height="1024" viewBox="0 0 2116 1024"><rect width="2116" height="1024" fill="#111D35"/><svg x="546" width="1024" height="1024">${mark.replace(/<svg[^>]*>/, '').replace(/<\/svg>\s*$/, '')}</svg></svg>`;
  await fs.writeFile(path.join(branding, 'music-sync-wide.svg'), wide);
  const generated = [];
  async function png(destination, svg, width, height = width, purpose = 'MSIX asset') {
    const buffer = await sharp(Buffer.from(svg)).resize(width, height).png({ compressionLevel: 9 }).toBuffer();
    await fs.writeFile(destination, buffer);
    const metadata = await sharp(buffer).metadata();
    if (metadata.width !== width || metadata.height !== height || !metadata.hasAlpha) throw new Error('Invalid PNG export: ' + destination);
    generated.push({ file: path.relative(root, destination).split(path.sep).join('/'), width, height, bytes: buffer.length, sha256: crypto.createHash('sha256').update(buffer).digest('hex'), purpose });
    return buffer;
  }
  const scales = [100, 125, 150, 200, 400];
  const families = [
    { name: 'StoreLogo', width: 50, height: 50, svg: icon },
    { name: 'Square44x44Logo', width: 44, height: 44, svg: icon },
    { name: 'Square150x150Logo', width: 150, height: 150, svg: icon },
    { name: 'Wide310x150Logo', width: 310, height: 150, svg: wide }
  ];
  for (const family of families) {
    await png(path.join(assets, family.name + '.png'), family.svg, family.width, family.height);
    for (const scale of scales) {
      await png(path.join(assets, `${family.name}.scale-${scale}.png`), family.svg,
        Math.ceil(family.width * scale / 100), Math.ceil(family.height * scale / 100));
    }
  }
  const targets = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256];
  const croppedMark = mark.replace('viewBox="0 0 1024 1024"', 'viewBox="128 144 768 768"');
  const croppedLight = light.replace('viewBox="0 0 1024 1024"', 'viewBox="128 144 768 768"');
  for (const size of targets) {
    await png(path.join(assets, `Square44x44Logo.targetsize-${size}.png`), icon, size);
    await png(path.join(assets, `Square44x44Logo.targetsize-${size}_altform-unplated.png`), croppedMark, size);
    await png(path.join(assets, `Square44x44Logo.targetsize-${size}_altform-lightunplated.png`), croppedLight, size);
  }

  // ICO: multiple PNG-compressed images, each rendered at its native size.
  const icoSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
  const icoImages = [];
  for (const size of icoSizes) icoImages.push(await sharp(Buffer.from(icon)).resize(size, size).png({ compressionLevel: 9 }).toBuffer());
  const header = Buffer.alloc(6 + 16 * icoImages.length);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(icoImages.length, 4);
  let offset = header.length;
  icoImages.forEach((buffer, i) => {
    const entry = 6 + 16 * i;
    header.writeUInt8(icoSizes[i] === 256 ? 0 : icoSizes[i], entry);
    header.writeUInt8(icoSizes[i] === 256 ? 0 : icoSizes[i], entry + 1);
    header.writeUInt16LE(1, entry + 4);
    header.writeUInt16LE(32, entry + 6);
    header.writeUInt32LE(buffer.length, entry + 8);
    header.writeUInt32LE(offset, entry + 12);
    offset += buffer.length;
  });
  const ico = Buffer.concat([header, ...icoImages]);
  await fs.writeFile(path.join(assets, 'AppIcon.ico'), ico);
  generated.push({ file: 'Packaging/Assets/AppIcon.ico', sizes: icoSizes, bytes: ico.length, sha256: crypto.createHash('sha256').update(ico).digest('hex'), purpose: 'Windows executable and notification area' });
  await png(path.join(store, 'StoreListingIcon-300.png'), icon, 300, 300, 'Partner Center 1:1 app tile');
  await png(path.join(store, 'StoreListingIcon-1024.png'), icon, 1024, 1024, 'High resolution design master; not a required listing size');
  await png(path.join(branding, 'music-sync-icon-1024.png'), icon, 1024, 1024, 'Design master');

  const preview = `<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="760"><rect width="1200" height="760" fill="#EEF2F8"/><text x="64" y="70" font-family="Segoe UI, sans-serif" font-size="34" font-weight="700" fill="#14223B">ESP32 MUSIC SYNC</text><text x="64" y="106" font-family="Segoe UI, sans-serif" font-size="19" fill="#53627A">Pulse Notes / Windows App + Microsoft Store</text><text x="76" y="168" font-family="Segoe UI, sans-serif" font-size="16" font-weight="600" fill="#53627A">WINDOWS APP</text><text x="700" y="168" font-family="Segoe UI, sans-serif" font-size="16" font-weight="600" fill="#53627A">STORE LISTING / 300 x 300</text><rect x="64" y="550" width="1072" height="154" rx="20" fill="#FFFFFF"/><text x="88" y="581" font-family="Segoe UI, sans-serif" font-size="14" fill="#53627A">NATIVE SIZES: 16 / 24 / 32 / 48 / 64</text><rect x="628" y="550" width="508" height="154" rx="20" fill="#14223B"/><text x="652" y="581" font-family="Segoe UI, sans-serif" font-size="14" fill="#B6C8E3">TRANSPARENT MARK / LIGHT + DARK</text></svg>`;
  const overlays = [
    { input: await sharp(Buffer.from(icon)).resize(318).png().toBuffer(), left: 72, top: 193 },
    { input: await sharp(Buffer.from(icon)).resize(300).png().toBuffer(), left: 700, top: 204 }
  ];
  let x = 90;
  for (const size of [16, 24, 32, 48, 64]) {
    overlays.push({ input: await sharp(Buffer.from(icon)).resize(size).png().toBuffer(), left: x, top: 610 + Math.round((64 - size) / 2) });
    x += size + 34;
  }
  overlays.push({ input: await sharp(Buffer.from(croppedLight)).resize(82).png().toBuffer(), left: 515, top: 596 });
  overlays.push({ input: await sharp(Buffer.from(croppedMark)).resize(82).png().toBuffer(), left: 840, top: 596 });
  await sharp(Buffer.from(preview)).composite(overlays).png({ compressionLevel: 9 }).toFile(path.join(branding, 'icon-preview.png'));
  await fs.writeFile(path.join(branding, 'assets-manifest.json'), JSON.stringify({ design: 'Pulse Notes', source: 'Original SVG geometry, no fonts or external artwork', assetCount: generated.length, icoSizes, assets: generated }, null, 2) + '\n');
  console.log(JSON.stringify({ design: 'Pulse Notes', assetCount: generated.length, icoBytes: ico.length, icoSizes, preview: path.join(branding, 'icon-preview.png') }, null, 2));
}
main().catch(error => { console.error(error.message); process.exitCode = 1; });
