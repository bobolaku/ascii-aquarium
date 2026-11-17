// 生成 aquarium.ico：一条彩色小鱼 + 气泡，32x32 / 32bpp，透明背景。
// 纯 Node、无依赖。先在 64x64 上绘制再 2x 降采样，得到平滑边缘。
const fs = require('fs');

const S = 64, OUT = 32;
const buf = new Float64Array(S * S * 4);   // RGBA, 0..255

function paint(x, y, c) {
  if (x < 0 || y < 0 || x >= S || y >= S || c[3] <= 0) return;
  const i = (y * S + x) * 4;
  const sa = c[3] / 255, da = buf[i + 3] / 255;
  const oa = sa + da * (1 - sa);
  if (oa <= 0) return;
  buf[i]     = (c[0] * sa + buf[i]     * da * (1 - sa)) / oa;
  buf[i + 1] = (c[1] * sa + buf[i + 1] * da * (1 - sa)) / oa;
  buf[i + 2] = (c[2] * sa + buf[i + 2] * da * (1 - sa)) / oa;
  buf[i + 3] = oa * 255;
}
function ellipse(cx, cy, rx, ry, fn) {
  for (let y = Math.floor(cy - ry - 1); y <= Math.ceil(cy + ry + 1); y++)
    for (let x = Math.floor(cx - rx - 1); x <= Math.ceil(cx + rx + 1); x++) {
      const d = ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2;
      if (d <= 1) paint(x, y, fn(x, y, d));
    }
}
function poly(pts, fn) {
  const xs = pts.map(p => p[0]), ys = pts.map(p => p[1]);
  const minX = Math.min(...xs), maxX = Math.max(...xs);
  const minY = Math.min(...ys), maxY = Math.max(...ys);
  for (let y = Math.floor(minY); y <= Math.ceil(maxY); y++)
    for (let x = Math.floor(minX); x <= Math.ceil(maxX); x++) {
      let inside = false;
      for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
        const [xi, yi] = pts[i], [xj, yj] = pts[j];
        if (((yi > y) !== (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi) + xi)) inside = !inside;
      }
      if (inside) paint(x, y, fn(x, y));
    }
}
function ring(cx, cy, r, w, c) {
  ellipse(cx, cy, r + w, r + w, (x, y) => {
    const d = Math.sqrt((x - cx) ** 2 + (y - cy) ** 2);
    return (d >= r - w * 0.5 && d <= r + w * 0.5) ? c : [0, 0, 0, 0];
  });
}

const TAIL = [244, 121, 63, 255];
const FIN  = [232, 105, 52, 255];
const BUB  = [143, 230, 255, 215];

// 尾巴在右 → 头在左（与 >°)))彡 一致）
poly([[42, 34], [60, 19], [60, 49]], () => TAIL);
poly([[21, 24], [35, 24], [28, 12]], () => FIN);   // 背鳍
poly([[23, 44], [33, 44], [28, 55]], () => FIN);   // 腹鳍

// 身体：橙色渐变 + 上缘高光 + 深色细描边
ellipse(27, 34, 17, 11, (x, y, d) => {
  if (d > 0.87) return [122, 62, 26, 255];
  const t = Math.max(0, Math.min(1, (x - 10) / 34));
  const hi = y < 30 ? 1.10 : 1;
  return [
    Math.min(255, Math.round((255 - 30 * t) * hi)),
    Math.min(255, Math.round((184 - 60 * t) * hi)),
    Math.min(255, Math.round((119 - 50 * t) * hi)),
    255
  ];
});

// 眼睛
ellipse(17, 30, 4.6, 4.6, () => [255, 255, 255, 255]);
ellipse(16.2, 30, 2.3, 2.3, () => [28, 42, 64, 255]);
ellipse(15.4, 29.2, 0.9, 0.9, () => [255, 255, 255, 255]);
// 嘴
poly([[9, 31], [13, 34], [9, 37]], () => [190, 84, 40, 255]);
// 气泡
ring(7, 16, 3.4, 0.9, BUB);
ring(3, 9, 2.4, 0.8, BUB);
ring(14, 6, 2.0, 0.7, BUB);

// 2x 超采样降采样成 32x32 BGRA（bottom-up）
const xor = Buffer.alloc(OUT * OUT * 4);
for (let y = 0; y < OUT; y++) for (let x = 0; x < OUT; x++) {
  let r = 0, g = 0, b = 0, a = 0;
  for (let dy = 0; dy < 2; dy++) for (let dx = 0; dx < 2; dx++) {
    const i = ((y * 2 + dy) * S + (x * 2 + dx)) * 4;
    const al = buf[i + 3] / 255;
    r += buf[i] * al; g += buf[i + 1] * al; b += buf[i + 2] * al; a += al;
  }
  const o = ((OUT - 1 - y) * OUT + x) * 4;
  xor[o]     = a > 0 ? Math.round(b / a) : 0;
  xor[o + 1] = a > 0 ? Math.round(g / a) : 0;
  xor[o + 2] = a > 0 ? Math.round(r / a) : 0;
  xor[o + 3] = Math.round(a / 4 * 255);
}

// AND 掩码（透明像素置 1），bottom-up，每行 4 字节
const andMask = Buffer.alloc(OUT * (OUT / 8));
for (let y = 0; y < OUT; y++) for (let x = 0; x < OUT; x++) {
  const o = ((OUT - 1 - y) * OUT + x) * 4;
  if (xor[o + 3] < 128) {
    const row = OUT - 1 - y;
    andMask[row * 4 + (x >> 3)] |= 0x80 >> (x & 7);
  }
}

// ICO = ICONDIR + ICONDIRENTRY + BITMAPINFOHEADER + XOR + AND
const dir = Buffer.alloc(22);
dir.writeUInt16LE(0, 0); dir.writeUInt16LE(1, 2); dir.writeUInt16LE(1, 4);
dir.writeUInt8(OUT, 6); dir.writeUInt8(OUT, 7);
dir.writeUInt8(0, 8); dir.writeUInt8(0, 9);
dir.writeUInt16LE(1, 10); dir.writeUInt16LE(32, 12);
dir.writeUInt32LE(40 + xor.length + andMask.length, 14);
dir.writeUInt32LE(22, 18);

const dib = Buffer.alloc(40);
dib.writeUInt32LE(40, 0);
dib.writeInt32LE(OUT, 4);
dib.writeInt32LE(OUT * 2, 8);   // XOR + AND 高度
dib.writeUInt16LE(1, 12);
dib.writeUInt16LE(32, 14);
dib.writeUInt32LE(0, 16);       // BI_RGB
dib.writeUInt32LE(xor.length, 20);

fs.writeFileSync('aquarium.ico', Buffer.concat([dir, dib, xor, andMask]));
console.log('aquarium.ico written:', 22 + 40 + xor.length + andMask.length, 'bytes');
