// Renders a raw RGBA tile buffer into a canvas at an integer zoom, with
// nearest-neighbour scaling so tiles stay crisp instead of turning to mush.

let scratch = null;

export async function drawWorld(canvas, streamRef, width, height, scale) {
    if (!canvas) return;

    const buffer = await streamRef.arrayBuffer();
    const bytes = new Uint8ClampedArray(buffer);

    // Reuse the offscreen surface between draws; reallocating a 4096-wide canvas
    // on every render is a noticeable hitch on large worlds.
    if (!scratch || scratch.width !== width || scratch.height !== height) {
        scratch = new OffscreenCanvas(width, height);
    }

    const src = scratch.getContext('2d');
    src.putImageData(new ImageData(bytes, width, height), 0, 0);

    canvas.width = Math.round(width * scale);
    canvas.height = Math.round(height * scale);

    const ctx = canvas.getContext('2d');
    ctx.imageSmoothingEnabled = false;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(scratch, 0, 0, canvas.width, canvas.height);
}
