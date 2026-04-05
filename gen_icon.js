// Generates app.ico for AI Browser Worker
// Design: dark circle + blue ring + lightning bolt (automation/worker)
const fs = require('fs');
const path = require('path');

function pointInPoly(px, py, poly) {
    let inside = false;
    for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
        const [xi, yi] = poly[i], [xj, yj] = poly[j];
        if ((yi > py) !== (yj > py) && px < ((xj - xi) * (py - yi)) / (yj - yi) + xi)
            inside = !inside;
    }
    return inside;
}

function renderFrame(size) {
    const data = new Uint8Array(size * size * 4);
    const S = size / 32;

    // Lightning bolt polygons (in 32x32 coord space)
    const upperBolt = [[12, 4], [20, 4], [15, 17], [10, 17]];
    const lowerBolt = [[17, 15], [22, 15], [20, 28], [12, 28]];

    for (let y = 0; y < size; y++) {
        for (let x = 0; x < size; x++) {
            const nx = (x + 0.5) / S;
            const ny = (y + 0.5) / S;
            const dx = nx - 16, dy = ny - 16;
            const d = Math.sqrt(dx * dx + dy * dy);
            const i = (y * size + x) * 4;

            let r = 0, g = 0, b = 0, a = 0;

            // Blue outer ring  r=13..15.5
            if (d <= 15.5 && d >= 13.0) {
                r = 97; g = 175; b = 239; a = 255;
            }
            // Outer glow  r=15.5..16.5
            if (d > 15.5 && d <= 16.5) {
                r = 97; g = 175; b = 239;
                a = Math.round((16.5 - d) * 180);
            }
            // Dark inner fill  r<13
            if (d < 13) {
                r = 28; g = 33; b = 40; a = 255;
            }

            // Lightning bolt (yellow) inside circle
            if (d < 13) {
                if (pointInPoly(nx, ny, upperBolt) || pointInPoly(nx, ny, lowerBolt)) {
                    r = 229; g = 192; b = 123; a = 255; // #E5C07B
                }
            }

            // Small blue dot highlights on ring
            const angle = Math.atan2(dy, dx);
            const snapAngles = [0, Math.PI / 2, Math.PI, -Math.PI / 2];
            const onSnap = snapAngles.some(sa => Math.abs(angle - sa) < 0.15);
            if (onSnap && d >= 13 && d <= 15.5) {
                r = 198; g = 120; b = 221; a = 255; // #C678DD purple accent
            }

            data[i] = r; data[i + 1] = g; data[i + 2] = b; data[i + 3] = a;
        }
    }
    return data;
}

function toBmpData(size, pixels) {
    const hdr = Buffer.alloc(40);
    hdr.writeUInt32LE(40, 0);
    hdr.writeInt32LE(size, 4);
    hdr.writeInt32LE(size * 2, 8); // ICO: XOR + AND stacked height
    hdr.writeUInt16LE(1, 12);
    hdr.writeUInt16LE(32, 14);
    // rest zero (BI_RGB)

    // Pixel data: BGRA, bottom-up
    const img = Buffer.alloc(size * size * 4);
    for (let y = 0; y < size; y++) {
        for (let x = 0; x < size; x++) {
            const s = (y * size + x) * 4;
            const d = ((size - 1 - y) * size + x) * 4;
            img[d]     = pixels[s + 2]; // B
            img[d + 1] = pixels[s + 1]; // G
            img[d + 2] = pixels[s + 0]; // R
            img[d + 3] = pixels[s + 3]; // A
        }
    }

    // AND mask (1-bit, bottom-up, rows padded to 4 bytes) — all 0 = use alpha
    const rowStride = Math.ceil(size / 32) * 4;
    const andMask = Buffer.alloc(rowStride * size, 0);

    return Buffer.concat([hdr, img, andMask]);
}

function writeIco(outPath) {
    const sizes = [16, 32, 48];
    const images = sizes.map(s => toBmpData(s, renderFrame(s)));

    const dir = Buffer.alloc(6);
    dir.writeUInt16LE(0, 0); // reserved
    dir.writeUInt16LE(1, 2); // type = icon
    dir.writeUInt16LE(sizes.length, 4);

    const entries = Buffer.alloc(sizes.length * 16);
    let offset = 6 + sizes.length * 16;
    for (let i = 0; i < sizes.length; i++) {
        const sz = sizes[i];
        entries.writeUInt8(sz, i * 16 + 0);
        entries.writeUInt8(sz, i * 16 + 1);
        entries.writeUInt8(0, i * 16 + 2);  // color count (0 = true color)
        entries.writeUInt8(0, i * 16 + 3);  // reserved
        entries.writeUInt16LE(1, i * 16 + 4);  // planes
        entries.writeUInt16LE(32, i * 16 + 6); // bit count
        entries.writeUInt32LE(images[i].length, i * 16 + 8);
        entries.writeUInt32LE(offset, i * 16 + 12);
        offset += images[i].length;
    }

    const out = Buffer.concat([dir, entries, ...images]);
    fs.mkdirSync(path.dirname(outPath), { recursive: true });
    fs.writeFileSync(outPath, out);
    console.log(`Created ${outPath} — ${out.length} bytes, sizes: ${sizes.join(', ')}px`);
}

writeIco(path.join(__dirname, 'src/WpfBrowserWorker/Resources/app.ico'));
