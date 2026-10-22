// Renders the four Warm Sound Cues (research 11, owner decision) to WAV, once, from the approved
// prototype's synthesis (.scratch/v4-overhaul/prototypes/11-overlay/project/Sounds.dc.html, the
// "warm" set): headless Chromium plays them into an OfflineAudioContext, and the samples are
// written as 16-bit mono PCM. The WAVs are committed and embedded in the exe (spec §7); run this
// again only if the sounds themselves change.
//
//   cd ui && node scripts/render-sound-cues.mjs
import { writeFileSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "@playwright/test";

const outDir = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "src", "VoiceCtrl", "Assets", "Sounds");
const SAMPLE_RATE = 48000;

// Runs in the page. `tone` is the prototype's, unchanged apart from taking the context.
function render(sampleRate) {
  const tone = (ac, t0, o) => {
    const osc = ac.createOscillator();
    osc.type = o.type || "sine";
    osc.frequency.setValueAtTime(o.f, t0);
    if (o.f2) osc.frequency.exponentialRampToValueAtTime(o.f2, t0 + (o.glide || o.dur));
    const g = ac.createGain();
    const vol = o.vol || 0.12;
    g.gain.setValueAtTime(0.0001, t0);
    g.gain.exponentialRampToValueAtTime(vol, t0 + (o.attack || 0.006));
    g.gain.exponentialRampToValueAtTime(0.0001, t0 + o.dur);
    let node = osc;
    if (o.lp) {
      const f = ac.createBiquadFilter();
      f.type = "lowpass";
      f.frequency.value = o.lp;
      osc.connect(f);
      node = f;
    }
    node.connect(g);
    g.connect(ac.destination);
    osc.start(t0);
    osc.stop(t0 + o.dur + 0.03);
  };

  // The "warm" set, as approved. Each starts 5 ms in, so the first sample is silence.
  const t = 0.005;
  const cues = {
    start: { seconds: 0.3, play: (ac) => tone(ac, t, { f: 262, f2: 392, glide: 0.12, dur: 0.24, vol: 0.16, type: "triangle", lp: 1200, attack: 0.02 }) },
    stop: { seconds: 0.3, play: (ac) => tone(ac, t, { f: 392, f2: 262, glide: 0.12, dur: 0.24, vol: 0.16, type: "triangle", lp: 1200, attack: 0.02 }) },
    done: {
      seconds: 0.56,
      play: (ac) => {
        tone(ac, t, { f: 262, dur: 0.5, vol: 0.12, type: "triangle", lp: 1400, attack: 0.02 });
        tone(ac, t, { f: 392, dur: 0.5, vol: 0.1, type: "triangle", lp: 1400, attack: 0.02 });
      },
    },
    failed: {
      seconds: 0.42,
      play: (ac) => {
        tone(ac, t, { f: 233, dur: 0.14, vol: 0.16, type: "triangle", lp: 900 });
        tone(ac, t + 0.16, { f: 233, dur: 0.2, vol: 0.16, type: "triangle", lp: 900 });
      },
    },
  };

  return Promise.all(
    Object.entries(cues).map(async ([name, cue]) => {
      const ac = new OfflineAudioContext(1, Math.ceil(cue.seconds * sampleRate), sampleRate);
      cue.play(ac);
      const buffer = await ac.startRendering();
      return [name, Array.from(buffer.getChannelData(0))];
    }),
  );
}

function wav(samples, sampleRate) {
  const data = Buffer.alloc(samples.length * 2);
  samples.forEach((s, i) => data.writeInt16LE(Math.round(Math.max(-1, Math.min(1, s)) * 32767), i * 2));
  const header = Buffer.alloc(44);
  header.write("RIFF", 0);
  header.writeUInt32LE(36 + data.length, 4);
  header.write("WAVE", 8);
  header.write("fmt ", 12);
  header.writeUInt32LE(16, 16);
  header.writeUInt16LE(1, 20); // PCM
  header.writeUInt16LE(1, 22); // mono
  header.writeUInt32LE(sampleRate, 24);
  header.writeUInt32LE(sampleRate * 2, 28);
  header.writeUInt16LE(2, 32);
  header.writeUInt16LE(16, 34);
  header.write("data", 36);
  header.writeUInt32LE(data.length, 40);
  return Buffer.concat([header, data]);
}

const browser = await chromium.launch();
try {
  const page = await browser.newPage();
  const rendered = await page.evaluate(render, SAMPLE_RATE);
  mkdirSync(outDir, { recursive: true });
  for (const [name, samples] of rendered) {
    const peak = Math.max(...samples.map(Math.abs));
    writeFileSync(join(outDir, `${name}.wav`), wav(samples, SAMPLE_RATE));
    console.log(`${name}.wav  ${(samples.length / SAMPLE_RATE).toFixed(2)} s  peak ${peak.toFixed(3)}`);
  }
} finally {
  await browser.close();
}
