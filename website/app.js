/**
 * Dynamic Island for Windows — Interactive Product Showcase Engine
 * Includes:
 * 1. AnalyticalSpring (exact port of Core/Spring.cs)
 * 2. Web Audio Micro-Haptics Synthesizer (exact frequencies/durations from Core/TactileFeedback.cs)
 * 3. Interactive Windows Desktop Island Stage (Notch Ears, Split-Pill Satellite, Drag-to-Shelf, Toast Queue)
 * 4. Living Bento Labs (Spring Curve Canvas, 4-Band FFT Canvas, Secret Redaction, Privacy Dots, Power Governor)
 * 5. Zero-RAM Modular Droplets Simulator (20 built-in droplets with live memory counter)
 * 6. Interactive Snell's Law Liquid Glass Optical Lens
 */

(() => {
  "use strict";

  const reducedMotionQuery = window.matchMedia("(prefers-reduced-motion: reduce)");

  // =========================================================================
  // 1. Closed-Form Analytical Spring (Core/Spring.cs)
  // =========================================================================
  class AnalyticalSpring {
    constructor(initial, stiffness = 320, damping = 26) {
      this.value = initial;
      this.target = initial;
      this.velocity = 0;
      this.stiffness = stiffness;
      this.damping = damping;
      this.restEpsilon = 0.05;
    }

    get isAtRest() {
      return (
        Math.abs(this.target - this.value) < this.restEpsilon &&
        Math.abs(this.velocity) < this.restEpsilon
      );
    }

    snapTo(val) {
      this.value = val;
      this.target = val;
      this.velocity = 0;
    }

    step(dt) {
      if (!Number.isFinite(dt) || dt <= 0) return;
      dt = Math.min(dt, 0.1);

      const x0 = this.value - this.target;
      const v0 = this.velocity;

      if (Math.abs(x0) < 1e-9 && Math.abs(v0) < 1e-9) {
        this.value = this.target;
        this.velocity = 0;
        return;
      }

      const omega0 = Math.sqrt(this.stiffness);
      const zeta = this.damping / (2.0 * omega0);
      const alpha = 0.5 * this.damping;

      let x = 0;
      let v = 0;

      if (zeta < 1.0 - 1e-5) {
        const omegaD = omega0 * Math.sqrt(1.0 - zeta * zeta);
        const a = x0;
        const b = (v0 + alpha * x0) / omegaD;
        const decay = Math.exp(-alpha * dt);
        const sin = Math.sin(omegaD * dt);
        const cos = Math.cos(omegaD * dt);
        x = decay * (a * cos + b * sin);
        v = decay * ((b * omegaD - alpha * a) * cos - (a * omegaD + alpha * b) * sin);
      } else if (zeta > 1.0 + 1e-5) {
        const omegaD = omega0 * Math.sqrt(zeta * zeta - 1.0);
        const r1 = -alpha + omegaD;
        const r2 = -alpha - omegaD;
        const c2 = (v0 - r1 * x0) / (r2 - r1);
        const c1 = x0 - c2;
        const e1 = Math.exp(r1 * dt);
        const e2 = Math.exp(r2 * dt);
        x = c1 * e1 + c2 * e2;
        v = c1 * r1 * e1 + c2 * r2 * e2;
      } else {
        const a = x0;
        const b = v0 + alpha * x0;
        const decay = Math.exp(-alpha * dt);
        x = decay * (a + b * dt);
        v = decay * (b - alpha * (a + b * dt));
      }

      this.value = this.target + x;
      this.velocity = v;

      if (this.isAtRest) {
        this.value = this.target;
        this.velocity = 0;
      }
    }
  }

  // =========================================================================
  // 2. Synthesized UI Micro-Haptics (Ported from Core/TactileFeedback.cs)
  // =========================================================================
  let hapticsEnabled = false;
  let audioCtx = null;

  function playHaptic(kind) {
    if (!hapticsEnabled && kind !== "force-tick" && kind !== "force-snap" && kind !== "force-dismiss") return;
    const presetKey = kind.replace("force-", "");
    const presets = {
      tick: { fStart: 520, fEnd: 460, decay: 110, ms: 11, amp: 0.16 },
      snap: { fStart: 320, fEnd: 240, decay: 75, ms: 16, amp: 0.22 },
      dismiss: { fStart: 380, fEnd: 190, decay: 60, ms: 22, amp: 0.24 }
    };
    const p = presets[presetKey] || presets.snap;
    try {
      const Ctx = window.AudioContext || window.webkitAudioContext;
      if (!Ctx) return;
      if (!audioCtx) audioCtx = new Ctx();
      if (audioCtx.state === "suspended") audioCtx.resume();

      const sr = audioCtx.sampleRate;
      const len = Math.floor(sr * (p.ms / 1000));
      const buf = audioCtx.createBuffer(1, len, sr);
      const data = buf.getChannelData(0);
      let phase = 0;
      for (let i = 0; i < len; i++) {
        const progress = i / len;
        const t = i / sr;
        const freq = p.fStart + (p.fEnd - p.fStart) * progress;
        phase += (2 * Math.PI * freq) / sr;
        data[i] = Math.sin(phase) * Math.exp(-p.decay * t) * p.amp;
      }
      const src = audioCtx.createBufferSource();
      src.buffer = buf;
      src.connect(audioCtx.destination);
      src.start();
    } catch {
      // Ignore Web Audio restrictions
    }
  }

  const hapticsToggleBtn = document.getElementById("haptics-toggle");
  if (hapticsToggleBtn) {
    hapticsToggleBtn.addEventListener("click", () => {
      hapticsEnabled = !hapticsEnabled;
      hapticsToggleBtn.setAttribute("aria-pressed", String(hapticsEnabled));
      if (hapticsEnabled) playHaptic("force-snap");
    });
  }

  // =========================================================================
  // 3. Interactive Desktop Stage & Dynamic Island Simulator
  // =========================================================================
  const bezelRowEl = document.getElementById("island-bezel-row");
  const pillEl = document.getElementById("island-pill");
  const contentEl = document.getElementById("island-content");
  const haloEl = document.getElementById("island-halo");
  const readoutEl = document.getElementById("spring-readout");
  const satelliteEl = document.getElementById("satellite-droplet");

  const dockButtons = document.querySelectorAll(".dock-btn");
  const notchBtn = document.getElementById("notch-mode-btn");
  const satelliteBtn = document.getElementById("satellite-btn");
  const styleBtn = document.getElementById("surface-style-btn");

  let globalStiffness = 320;
  let globalDamping = 26;

  const springW = new AnalyticalSpring(184, globalStiffness, globalDamping);
  const springH = new AnalyticalSpring(36, globalStiffness * 0.92, globalDamping * 0.96);
  const springR = new AnalyticalSpring(18, globalStiffness * 0.92, globalDamping * 0.96);
  const springSat = new AnalyticalSpring(0, globalStiffness * 0.92, globalDamping * 0.94);

  let rafId = null;
  let lastTs = null;
  let currentMode = "idle";
  let isLiquidGlass = true;
  let isNotchMode = false;
  let isSatelliteActive = false;
  let mediaPlaying = true;
  let mediaAccentHex = "#ff9f0a";
  let timerRunning = true;
  let timerSeconds = 25 * 60;
  let stagedFiles = ["spec_v0.9.pdf", "release.zip"];
  let toastTimeout = null;

  const MODES = {
    idle: {
      w: 184,
      h: 36,
      r: 18,
      halo: "radial-gradient(circle, rgba(48, 209, 88, 0.25) 0%, transparent 72%)",
      render: () => `
        <div class="sim-idle-row">
          <span class="privacy-dots">
            <span class="dot-cam" title="Camera active"></span>
            <span class="dot-mic" title="Microphone active"></span>
            <span style="font-size:0.72rem; color:#a1a1a6;">Idle</span>
          </span>
          <span class="camera-lens" aria-hidden="true"></span>
          <span style="font-variant-numeric:tabular-nums; font-size:0.75rem;">1:28 AM</span>
        </div>`
    },
    media: {
      w: 404,
      h: 146,
      r: 28,
      halo: () => `radial-gradient(circle, ${mediaAccentHex}55 0%, rgba(255, 51, 102, 0.14) 55%, transparent 74%)`,
      render: () => `
        <div class="sim-media-layout">
          <div class="sim-media-top">
            <div class="album-art" style="background: linear-gradient(135deg, ${mediaAccentHex}, #5e5ce6);" aria-hidden="true">
              <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2.2">
                <path d="M9 18V5l12-2v13"></path><circle cx="6" cy="18" r="3"></circle><circle cx="18" cy="16" r="3"></circle>
              </svg>
            </div>
            <div class="media-meta">
              <div class="media-title">Midnight City (WASAPI Loopback)</div>
              <div class="media-artist">Spotify · 4-Band FFT · HDR Glow</div>
            </div>
            <div class="eq-bars" aria-hidden="true" style="opacity:${mediaPlaying ? 1 : 0.3}">
              <span class="eq-bar" style="background:${mediaAccentHex};"></span>
              <span class="eq-bar" style="background:${mediaAccentHex};"></span>
              <span class="eq-bar" style="background:${mediaAccentHex};"></span>
              <span class="eq-bar" style="background:${mediaAccentHex};"></span>
            </div>
          </div>
          <div class="sim-progress-track"><div class="sim-progress-fill" style="background:${mediaAccentHex};"></div></div>
          <div class="sim-media-controls">
            <span>2:14 / 3:38</span>
            <div class="pill-action-row">
              <button class="pill-mini-btn" type="button" data-sim-action="toggle-play">${mediaPlaying ? "Pause" : "Play"}</button>
              <button class="pill-mini-btn" type="button" data-sim-action="cycle-source">Source: Spotify</button>
            </div>
          </div>
        </div>`
    },
    agent: {
      w: 452,
      h: 174,
      r: 28,
      halo: "radial-gradient(circle, rgba(94, 92, 230, 0.42) 0%, rgba(10, 132, 255, 0.14) 55%, transparent 74%)",
      render: () => `
        <div class="sim-agent-layout">
          <div class="sim-agent-head">
            <div>
              <div style="font-size:0.88rem; font-weight:700;">Antigravity · Approval Requested</div>
              <div style="font-size:0.73rem; color:#a1a1a6;">\\\\.\\pipe\\DynamicIsland.Activities</div>
            </div>
            <div class="pill-action-row">
              <button class="pill-mini-btn approve" type="button" data-sim-action="agent-approve">Approve</button>
              <button class="pill-mini-btn reject" type="button" data-sim-action="agent-reject">Reject</button>
            </div>
          </div>
          <div class="phase-chips">
            <span class="phase-chip done">1. Analyze</span>
            <span class="phase-chip active">2. Patch</span>
            <span class="phase-chip">3. --self-test</span>
          </div>
          <div class="sim-diff-line">+ ValidateJwtIssuer(token, strict: true);</div>
        </div>`
    },
    timer: {
      w: 332,
      h: 92,
      r: 24,
      halo: "radial-gradient(circle, rgba(255, 159, 10, 0.34) 0%, transparent 72%)",
      render: () => {
        const m = String(Math.floor(timerSeconds / 60)).padStart(2, "0");
        const s = String(timerSeconds % 60).padStart(2, "0");
        return `
          <div style="width:100%; display:flex; align-items:center; justify-content:space-between; gap:12px;">
            <div style="text-align:left;">
              <div style="font-size:0.72rem; color:#ff9f0a; font-weight:700; text-transform:uppercase; letter-spacing:0.05em;">Pomodoro Focus</div>
              <div style="font-size:1.5rem; font-weight:700; font-variant-numeric:tabular-nums;">${m}:${s}</div>
            </div>
            <div class="pill-action-row">
              <button class="pill-mini-btn" type="button" data-sim-action="toggle-timer">${timerRunning ? "Pause" : "Resume"}</button>
              <button class="pill-mini-btn" type="button" data-sim-action="reset-timer">25m</button>
            </div>
          </div>`;
      }
    },
    shelf: {
      w: 440,
      h: 136,
      r: 26,
      halo: "radial-gradient(circle, rgba(50, 173, 230, 0.34) 0%, transparent 74%)",
      render: () => `
        <div style="width:100%; display:flex; flex-direction:column; gap:10px; text-align:left;">
          <div style="display:flex; align-items:center; justify-content:space-between;">
            <span style="font-size:0.86rem; font-weight:700;">Notch Drop Shelf (${stagedFiles.length} staged)</span>
            <button class="pill-mini-btn" type="button" data-sim-action="clear-shelf">Clear</button>
          </div>
          <div style="display:flex; flex-wrap:wrap; gap:6px;">
            ${stagedFiles.length ? stagedFiles.map((f) => `<span class="phase-chip">${f}</span>`).join("") : `<span class="phase-chip">Drop any file chip below onto the Island</span>`}
            <span class="phase-chip" style="color:#ffd479;">AKIA••••••••MPLE</span>
          </div>
        </div>`
    },
    vitals: {
      w: 410,
      h: 118,
      r: 26,
      halo: "radial-gradient(circle, rgba(48, 209, 88, 0.28) 0%, transparent 74%)",
      render: () => `
        <div style="width:100%; display:flex; align-items:center; justify-content:space-between; gap:14px; text-align:left;">
          <div>
            <div style="font-size:0.74rem; color:#30d158; font-weight:700;">Open-Meteo · 24°C Clear</div>
            <div style="font-size:0.96rem; font-weight:700;">CPU 11% · RAM 42% · iGPU</div>
            <div style="font-size:0.74rem; color:#a1a1a6;">Next: Design Sync · 2:00 PM</div>
          </div>
          <span class="phase-chip done">0.08% Idle</span>
        </div>`
    }
  };

  function getClampedWidth(desiredW) {
    const parentW = bezelRowEl?.clientWidth || window.innerWidth;
    return Math.min(desiredW, Math.max(220, parentW - 24));
  }

  function applyGeometry() {
    if (!pillEl) return;
    pillEl.style.width = `${Math.round(springW.value)}px`;
    pillEl.style.height = `${Math.round(springH.value)}px`;
    pillEl.style.borderRadius = `${Math.round(springR.value)}px`;

    if (satelliteEl) {
      const satT = Math.max(0, Math.min(1.2, springSat.value));
      satelliteEl.style.width = `${Math.round(92 * satT)}px`;
      satelliteEl.style.marginLeft = `${Math.round(10 * satT)}px`;
      satelliteEl.style.opacity = String(Math.min(1, satT));
      satelliteEl.classList.toggle("active", satT > 0.5);
      satelliteEl.setAttribute("aria-hidden", satT < 0.2 ? "true" : "false");
    }

    if (readoutEl) {
      const zeta = (globalDamping / (2 * Math.sqrt(globalStiffness))).toFixed(2);
      readoutEl.textContent = `W: ${Math.round(springW.value)}px · H: ${Math.round(springH.value)}px · ζ = ${zeta}`;
    }
  }

  function stepAnimation(ts) {
    if (lastTs === null) lastTs = ts;
    const dt = (ts - lastTs) / 1000;
    lastTs = ts;

    springW.step(dt);
    springH.step(dt);
    springR.step(dt);
    springSat.step(dt);
    applyGeometry();

    if (springW.isAtRest && springH.isAtRest && springR.isAtRest && springSat.isAtRest) {
      rafId = null;
      lastTs = null;
      return;
    }
    rafId = requestAnimationFrame(stepAnimation);
  }

  function kickSprings() {
    if (reducedMotionQuery.matches) {
      springW.snapTo(springW.target);
      springH.snapTo(springH.target);
      springR.snapTo(springR.target);
      springSat.snapTo(springSat.target);
      applyGeometry();
      return;
    }
    if (!rafId) {
      lastTs = null;
      rafId = requestAnimationFrame(stepAnimation);
    }
  }

  function setMode(mode, withHaptic = true) {
    const cfg = MODES[mode] || MODES.idle;
    currentMode = mode;
    if (toastTimeout) {
      clearTimeout(toastTimeout);
      toastTimeout = null;
    }

    dockButtons.forEach((b) => b.setAttribute("aria-pressed", b.dataset.mode === mode ? "true" : "false"));

    springW.target = getClampedWidth(cfg.w);
    springH.target = cfg.h;
    springR.target = cfg.r;
    kickSprings();

    if (haloEl) haloEl.style.background = typeof cfg.halo === "function" ? cfg.halo() : cfg.halo;
    if (contentEl) contentEl.innerHTML = cfg.render();
    if (withHaptic) playHaptic("snap");
  }

  dockButtons.forEach((btn) => {
    btn.addEventListener("click", () => setMode(btn.dataset.mode, true));
  });

  if (notchBtn) {
    notchBtn.addEventListener("click", () => {
      isNotchMode = !isNotchMode;
      notchBtn.setAttribute("aria-pressed", String(isNotchMode));
      pillEl?.classList.toggle("notch-mode", isNotchMode);
      bezelRowEl?.classList.toggle("notch-attached", isNotchMode);
      playHaptic("tick");
    });
  }

  if (satelliteBtn) {
    satelliteBtn.addEventListener("click", () => {
      isSatelliteActive = !isSatelliteActive;
      satelliteBtn.setAttribute("aria-pressed", String(isSatelliteActive));
      springSat.target = isSatelliteActive ? 1.0 : 0.0;
      kickSprings();
      playHaptic("snap");
    });
  }

  if (styleBtn) {
    styleBtn.addEventListener("click", () => {
      isLiquidGlass = !isLiquidGlass;
      styleBtn.setAttribute("aria-pressed", String(isLiquidGlass));
      styleBtn.textContent = isLiquidGlass ? "Liquid Glass" : "Classic OLED";
      pillEl?.classList.toggle("classic-style", !isLiquidGlass);
      playHaptic("tick");
    });
  }

  // Interactive Controls Inside Pill
  contentEl?.addEventListener("click", (e) => {
    const btn = e.target.closest("[data-sim-action]");
    if (!btn) return;
    const act = btn.dataset.simAction;
    if (act === "toggle-play") {
      mediaPlaying = !mediaPlaying;
      setMode("media", false);
      playHaptic("tick");
    } else if (act === "cycle-source") {
      btn.textContent = btn.textContent.includes("Spotify") ? "Source: Chrome" : "Source: Spotify";
      playHaptic("tick");
    } else if (act === "toggle-timer") {
      timerRunning = !timerRunning;
      setMode("timer", false);
      playHaptic("tick");
    } else if (act === "reset-timer") {
      timerSeconds = 25 * 60;
      timerRunning = true;
      setMode("timer", false);
      playHaptic("snap");
    } else if (act === "agent-approve") {
      contentEl.innerHTML = `<div style="font-size:0.86rem; font-weight:600; color:#7ef29a;">Approved via Named Pipe RPC · Returning control to Antigravity</div>`;
      playHaptic("snap");
    } else if (act === "agent-reject") {
      contentEl.innerHTML = `<div style="font-size:0.86rem; font-weight:600; color:#ff8b84;">Rejected patch · Task paused safely</div>`;
      playHaptic("dismiss");
    } else if (act === "clear-shelf") {
      stagedFiles = [];
      setMode("shelf", false);
      playHaptic("dismiss");
    }
  });

  // Drag-and-Drop onto Dynamic Island Pill
  document.querySelectorAll("[data-drop-file]").forEach((chip) => {
    const fileName = chip.dataset.dropFile;
    chip.addEventListener("dragstart", (e) => {
      e.dataTransfer?.setData("text/plain", fileName);
    });
    chip.addEventListener("click", () => {
      if (!stagedFiles.includes(fileName)) stagedFiles.push(fileName);
      setMode("shelf", true);
    });
  });

  if (pillEl) {
    pillEl.addEventListener("dragover", (e) => {
      e.preventDefault();
      pillEl.classList.add("drag-over");
    });
    pillEl.addEventListener("dragleave", () => pillEl.classList.remove("drag-over"));
    pillEl.addEventListener("drop", (e) => {
      e.preventDefault();
      pillEl.classList.remove("drag-over");
      const file = e.dataTransfer?.getData("text/plain");
      if (file && !stagedFiles.includes(file)) stagedFiles.push(file);
      setMode("shelf", true);
    });
  }

  // Toast Priority Queue Tester (Critical > Notification > Feedback)
  const TOASTS = {
    volume: {
      priority: 1,
      w: 250,
      h: 44,
      r: 22,
      halo: "radial-gradient(circle, rgba(255,255,255,0.2) 0%, transparent 72%)",
      html: `<div class="sim-idle-row"><span>Volume</span><div style="flex:1;height:5px;border-radius:99px;background:rgba(255,255,255,0.16);overflow:hidden;"><div style="width:68%;height:100%;background:#f5f5f7;"></div></div><span>68%</span></div>`
    },
    message: {
      priority: 2,
      w: 360,
      h: 64,
      r: 26,
      halo: "radial-gradient(circle, rgba(48,209,88,0.34) 0%, transparent 72%)",
      html: `<div class="sim-idle-row"><span><strong style="color:#30d158;">WhatsApp</strong> · Alex: Ship v0.10.0?</span><span class="phase-chip done">Notification</span></div>`
    },
    critical: {
      priority: 3,
      w: 350,
      h: 64,
      r: 26,
      halo: "radial-gradient(circle, rgba(255,69,58,0.44) 0%, transparent 72%)",
      html: `<div class="sim-idle-row"><span><strong style="color:#ff453a;">Battery 12%</strong> · Saver Engaged</span><span class="phase-chip" style="background:rgba(255,69,58,0.24);color:#ff8b84;">Critical</span></div>`
    }
  };

  document.querySelectorAll("[data-toast]").forEach((btn) => {
    btn.addEventListener("click", () => {
      const t = TOASTS[btn.dataset.toast];
      if (!t) return;
      springW.target = getClampedWidth(t.w);
      springH.target = t.h;
      springR.target = t.r;
      kickSprings();
      if (haloEl) haloEl.style.background = t.halo;
      if (contentEl) contentEl.innerHTML = t.html;
      playHaptic(btn.dataset.toast === "critical" ? "snap" : "tick");
      if (toastTimeout) clearTimeout(toastTimeout);
      toastTimeout = setTimeout(() => setMode(currentMode, false), 2600);
    });
  });

  const timerInterval = setInterval(() => {
    if (currentMode === "timer" && timerRunning && timerSeconds > 0 && !toastTimeout) {
      timerSeconds -= 1;
      if (contentEl) contentEl.innerHTML = MODES.timer.render();
    }
  }, 1000);

  window.addEventListener("resize", () => setMode(currentMode, false), { passive: true });
  setMode("idle", false);

  // =========================================================================
  // 4. Living Bento Labs (Spring Curve Canvas + FFT Canvas + Clipboard + Privacy + Power)
  // =========================================================================
  const springCanvas = document.getElementById("spring-canvas");
  const stiffnessSlider = document.getElementById("stiffness-slider");
  const dampingSlider = document.getElementById("damping-slider");
  const stiffnessValEl = document.getElementById("stiffness-val");
  const dampingValEl = document.getElementById("damping-val");
  const zetaBadgeEl = document.getElementById("zeta-badge");

  function drawSpringCurve() {
    if (!springCanvas) return;
    const ctx = springCanvas.getContext("2d");
    if (!ctx) return;

    const w = springCanvas.width;
    const h = springCanvas.height;
    ctx.clearRect(0, 0, w, h);

    // Target line
    const targetY = h * 0.28;
    ctx.strokeStyle = "rgba(255,255,255,0.16)";
    ctx.setLineDash([4, 4]);
    ctx.beginPath();
    ctx.moveTo(0, targetY);
    ctx.lineTo(w, targetY);
    ctx.stroke();
    ctx.setLineDash([]);

    // Simulate step response from 0 -> 1 over 1.2s at 240Hz
    const sim = new AnalyticalSpring(0, globalStiffness, globalDamping);
    sim.target = 1;
    const dt = 1 / 240;
    const totalTime = 1.1;
    const steps = Math.floor(totalTime / dt);

    ctx.strokeStyle = "#ff9f0a";
    ctx.lineWidth = 2.5;
    ctx.beginPath();
    for (let i = 0; i <= steps; i++) {
      const x = (i / steps) * w;
      const y = h * 0.86 - sim.value * (h * 0.58);
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
      sim.step(dt);
    }
    ctx.stroke();
  }

  function updateSpringParams() {
    globalStiffness = Number(stiffnessSlider?.value || 320);
    globalDamping = Number(dampingSlider?.value || 26);
    if (stiffnessValEl) stiffnessValEl.textContent = String(globalStiffness);
    if (dampingValEl) dampingValEl.textContent = String(globalDamping);

    const zeta = globalDamping / (2 * Math.sqrt(globalStiffness));
    const regime = zeta < 0.98 ? "Underdamped" : zeta > 1.02 ? "Overdamped" : "Critically Damped";
    if (zetaBadgeEl) zetaBadgeEl.textContent = `ζ = ${zeta.toFixed(2)} · ${regime}`;

    springW.stiffness = globalStiffness;
    springW.damping = globalDamping;
    springH.stiffness = globalStiffness * 0.92;
    springH.damping = globalDamping * 0.96;
    springR.stiffness = globalStiffness * 0.92;
    springR.damping = globalDamping * 0.96;
    springSat.stiffness = globalStiffness * 0.92;
    springSat.damping = globalDamping * 0.94;

    drawSpringCurve();
  }

  stiffnessSlider?.addEventListener("input", updateSpringParams);
  dampingSlider?.addEventListener("input", updateSpringParams);
  updateSpringParams();

  // Lab 2: 4-Band WASAPI FFT Canvas
  const fftCanvas = document.getElementById("fft-canvas");
  let fftPhase = 0;
  function renderFft() {
    if (!fftCanvas) return;
    const ctx = fftCanvas.getContext("2d");
    if (!ctx) return;
    const w = fftCanvas.width;
    const h = fftCanvas.height;
    ctx.clearRect(0, 0, w, h);

    const bands = 16;
    const gap = 8;
    const barW = (w - gap * (bands + 1)) / bands;
    for (let i = 0; i < bands; i++) {
      const amp = reducedMotionQuery.matches
        ? 0.45 + 0.35 * Math.sin(i * 0.7)
        : 0.28 + 0.62 * Math.abs(Math.sin(fftPhase * 1.4 + i * 0.55) * Math.cos(fftPhase * 0.6 + i * 0.25));
      const barH = Math.max(10, amp * (h - 24));
      const x = gap + i * (barW + gap);
      const y = h - barH - 8;
      ctx.fillStyle = mediaAccentHex;
      ctx.beginPath();
      ctx.roundRect(x, y, barW, barH, 4);
      ctx.fill();
    }
    fftPhase += 0.05;
    if (!reducedMotionQuery.matches) requestAnimationFrame(renderFft);
  }
  renderFft();

  document.querySelectorAll(".swatch-btn").forEach((btn) => {
    btn.addEventListener("click", () => {
      document.querySelectorAll(".swatch-btn").forEach((b) => b.setAttribute("aria-pressed", "false"));
      btn.setAttribute("aria-pressed", "true");
      mediaAccentHex = btn.dataset.accent || "#ff9f0a";
      if (currentMode === "media") setMode("media", false);
      if (reducedMotionQuery.matches) renderFft();
      playHaptic("tick");
    });
  });

  // Lab 3: Clipboard Redaction & Transforms
  const clipOut = document.getElementById("clip-demo-output");
  const CLIP_MODES = {
    aws: "AKIA••••••••MPLE (Auto-masked on UI)",
    json: '{\n  "pipe": "DynamicIsland.Activities",\n  "ipc": 2\n}',
    b64: 'Decode("Rmx1aWQ="): "Fluid"'
  };
  document.querySelectorAll("[data-clip]").forEach((btn) => {
    btn.addEventListener("click", () => {
      document.querySelectorAll("[data-clip]").forEach((b) => b.setAttribute("aria-pressed", "false"));
      btn.setAttribute("aria-pressed", "true");
      if (clipOut) clipOut.textContent = CLIP_MODES[btn.dataset.clip] || "";
      playHaptic("tick");
    });
  });

  // Lab 4: Kernel Privacy & Mic Mute
  const labMicBtn = document.getElementById("lab-mic-btn");
  const labMicDot = document.getElementById("lab-mic-dot");
  const labCallStatus = document.getElementById("lab-call-status");
  let micMuted = false;
  labMicBtn?.addEventListener("click", () => {
    micMuted = !micMuted;
    labMicBtn.setAttribute("aria-pressed", String(micMuted));
    labMicBtn.textContent = micMuted ? "Unmute Mic" : "Mute Mic";
    if (labMicDot) {
      labMicDot.style.background = micMuted ? "#ff453a" : "#ff9f0a";
      labMicDot.style.boxShadow = micMuted ? "0 0 8px #ff453a" : "0 0 8px #ff9f0a";
    }
    if (labCallStatus) {
      labCallStatus.textContent = micMuted ? "Teams Call · Mic Muted" : "Teams Call · 04:18";
    }
    playHaptic("snap");
  });

  // Lab 5: Power Governor & Synthesized Micro-Haptics
  const powerOut = document.getElementById("power-demo-output");
  document.querySelectorAll("[data-power]").forEach((btn) => {
    btn.addEventListener("click", () => {
      document.querySelectorAll("[data-power]").forEach((b) => b.setAttribute("aria-pressed", "false"));
      btn.setAttribute("aria-pressed", "true");
      if (powerOut) {
        powerOut.textContent =
          btn.dataset.power === "saver"
            ? "Battery Saver · WASAPI Suspended · Polling 3x Relaxed"
            : "240Hz Springs · WASAPI Active · iGPU Pinned";
      }
      playHaptic("tick");
    });
  });

  document.querySelectorAll("[data-synth]").forEach((btn) => {
    btn.addEventListener("click", () => {
      playHaptic(`force-${btn.dataset.synth}`);
    });
  });

  // =========================================================================
  // 5. Built-in 20 Droplets Explorer with Real Zero-RAM Toggles
  // =========================================================================
  const DROPLETS = [
    { id: "jiggle_basket", name: "Jiggle Floating Basket", badge: "Floating Basket", category: "Productivity", ramKb: 48, enabled: true, desc: "Shake cursor while dragging files to summon a floating drop basket anywhere." },
    { id: "file_converter", name: "File Actions & PDF", badge: "File Tools", category: "Productivity", ramKb: 0, enabled: true, desc: "Convert images (PNG/JPG/WebP), images to PDF, merge PDFs, compress video, unzip." },
    { id: "element_capture", name: "Element Capture & Pin", badge: "Screen Pin", category: "Capture", ramKb: 0, enabled: true, desc: "Region screenshot with arrow/box/blur/text annotation, instant OCR & pin-to-screen." },
    { id: "clipboard_pro", name: "Clipboard Picker & Queue", badge: "Clipboard Queue", category: "Productivity", ramKb: 64, enabled: true, desc: "Cursor popup picker (Ctrl+Alt+Shift+V), Plain Text paste & sequential Paste Queue." },
    { id: "caffeine", name: "Caffeine Pro", badge: "Power Assist", category: "System", ramKb: 16, enabled: true, desc: "Prevent sleep for 15m / 1h / forever / while on AC / until a target process exits." },
    { id: "drop_shelf", name: "Notch Drop Shelf", badge: "Notch Tray", category: "Productivity", ramKb: 120, enabled: true, desc: "Top-edge file staging tray with thumbnails, batch zip/unzip & pinned history." },
    { id: "quick_calc", name: "Quick Calc & Units", badge: "Natural Math", category: "Productivity", ramKb: 12, enabled: true, desc: "Natural-language math, unit/currency/base conversions & 'ans' memory." },
    { id: "voice_memo", name: "Voice Memo Recorder", badge: "Audio WAV", category: "Media", ramKb: 32, enabled: true, desc: "One-click microphone WAV recorder with live waveform & shelf export." },
    { id: "system_hud", name: "System Vitals & RAM Buster", badge: "Vitals Telemetry", category: "System", ramKb: 96, enabled: true, desc: "Zero-allocation CPU/RAM telemetry, top process inspector & standby RAM purge." },
    { id: "peripherals", name: "AirPods & Bluetooth HUD", badge: "Bluetooth HUD", category: "System", ramKb: 64, enabled: true, desc: "Live battery levels & connection alerts for headphones, mice & gamepads." },
    { id: "notes", name: "Markdown Quick Notes", badge: "Markdown Scratch", category: "Productivity", ramKb: 24, enabled: true, desc: "Fast offline markdown scratchpad, pinning, color tags & Drop Shelf .md export." },
    { id: "widgets_gallery", name: "Widgets Gallery", badge: "Bento Dashboard", category: "System", ramKb: 28, enabled: true, desc: "Bento-style live dashboard with system vitals, battery, network and media glanceables." },
    { id: "webcam_mirror", name: "Webcam Mirror", badge: "Pre-Call Check", category: "Capture", ramKb: 0, enabled: true, desc: "1-click pre-call camera preview, screen ring light & live mic check." },
    { id: "quick_launcher", name: "Quick App & Folder Launcher", badge: "App Runner", category: "Productivity", ramKb: 20, enabled: true, desc: "1-click app launcher with file drop-to-open capabilities." },
    { id: "termi_notch", name: "Termi-Notch Snippets", badge: "Shell Snippets", category: "Productivity", ramKb: 32, enabled: true, desc: "Saved PowerShell/CMD snippets with live streaming output in Dynamic Island." },
    { id: "window_snap", name: "Window Snap Droplet", badge: "Smart Halves", category: "Productivity", ramKb: 16, enabled: true, desc: "Smart multi-monitor window snapping to halves and quarters on drag." },
    { id: "voice_transcription", name: "Voice Transcription", badge: "Offline STT", category: "Media", ramKb: 0, enabled: true, desc: "Offline speech-to-text recognition for voice memos with 1-click Notes export." },
    { id: "mech_sounds", name: "Mechanical Keyboard Sounds", badge: "Cherry/Thock", category: "System", ramKb: 40, enabled: false, desc: "Tactile Cherry Blue, Brown Thock & Typewriter audio feedback with organic pitch jitter." },
    { id: "phone_bridge", name: "Phone Bridge & LAN Quickshare", badge: "LAN Wi-Fi Share", category: "Productivity", ramKb: 16, enabled: true, desc: "Send & receive files and sync clipboard with any iOS/Android phone over local Wi-Fi." },
    { id: "pet_sanctuary", name: "Pet Sanctuary & Playground", badge: "Companion", category: "Productivity", ramKb: 28, enabled: true, desc: "Interactive desktop companion with unique species toys, tricks, and desktop roam." }
  ];

  const dropletsGrid = document.getElementById("droplets-grid");
  const filterTabs = document.querySelectorAll(".filter-tab");
  const dropletSearch = document.getElementById("droplet-search");
  const ramSummaryEl = document.getElementById("droplet-ram-summary");
  const dropletResetBtn = document.getElementById("droplet-reset-btn");
  let activeCategory = "All";

  function renderDroplets() {
    if (!dropletsGrid) return;
    const q = (dropletSearch?.value || "").trim().toLowerCase();
    const filtered = DROPLETS.filter((d) => {
      const catOk = activeCategory === "All" || d.category === activeCategory;
      const qOk = !q || d.name.toLowerCase().includes(q) || d.desc.toLowerCase().includes(q) || d.badge.toLowerCase().includes(q);
      return catOk && qOk;
    });

    const totalActiveKb = DROPLETS.reduce((sum, d) => sum + (d.enabled ? d.ramKb : 0), 0);

    dropletsGrid.innerHTML = filtered
      .map(
        (d) => `
      <article class="droplet-card ${d.enabled ? "" : "disabled"}">
        <div>
          <div class="droplet-head">
            <h3 class="droplet-name">${d.name}</h3>
            <button class="di-switch" type="button" role="switch" aria-checked="${d.enabled}" aria-label="Toggle ${d.name}" data-toggle-droplet="${d.id}"></button>
          </div>
          <p class="droplet-desc">${d.desc}</p>
        </div>
        <div class="droplet-foot">
          <span class="droplet-ram">${!d.enabled ? "Off (0 RAM)" : d.ramKb === 0 ? "0 KB idle" : `~${d.ramKb} KB active`}</span>
          <span class="droplet-badge">${d.badge}</span>
        </div>
      </article>`
      )
      .join("");

    if (ramSummaryEl) {
      ramSummaryEl.innerHTML = `${filtered.length} shown · Active Footprint: <strong>~${totalActiveKb} KB</strong>`;
    }
  }

  dropletsGrid?.addEventListener("click", (e) => {
    const sw = e.target.closest("[data-toggle-droplet]");
    if (!sw) return;
    const item = DROPLETS.find((d) => d.id === sw.dataset.toggleDroplet);
    if (!item) return;
    item.enabled = !item.enabled;
    playHaptic("tick");
    renderDroplets();
  });

  filterTabs.forEach((tab) => {
    tab.addEventListener("click", () => {
      filterTabs.forEach((t) => t.setAttribute("aria-selected", "false"));
      tab.setAttribute("aria-selected", "true");
      activeCategory = tab.dataset.category;
      renderDroplets();
    });
  });

  dropletSearch?.addEventListener("input", renderDroplets);
  dropletResetBtn?.addEventListener("click", () => {
    DROPLETS.forEach((d) => {
      d.enabled = d.id !== "mech_sounds";
    });
    playHaptic("snap");
    renderDroplets();
  });

  renderDroplets();

  // =========================================================================
  // 6. Interactive Liquid Glass Optical Lens
  // =========================================================================
  const opticalPlayground = document.getElementById("optical-playground");
  const opticalLens = document.getElementById("optical-lens");
  const iorSlider = document.getElementById("ior-slider");
  const dispSlider = document.getElementById("disp-slider");
  const iorValEl = document.getElementById("ior-val");
  const dispValEl = document.getElementById("disp-val");
  const iorReadout = document.getElementById("ior-readout");

  function updateOpticalLens() {
    const ior = Number(iorSlider?.value || 1.45);
    const disp = Number(dispSlider?.value || 0.35);
    if (iorValEl) iorValEl.textContent = ior.toFixed(2);
    if (dispValEl) dispValEl.textContent = disp.toFixed(2);
    if (iorReadout) iorReadout.textContent = `IOR: ${ior.toFixed(2)} · Dispersion: ${disp.toFixed(2)}`;
    if (opticalLens) {
      const shift = Math.round(disp * 12);
      const blurPx = Math.round(4 + (ior - 1.0) * 10);
      opticalLens.style.backdropFilter = `blur(${blurPx}px) saturate(185%) contrast(1.12)`;
      opticalLens.style.boxShadow = `0 24px 50px rgba(0,0,0,0.65), inset 0 2px 4px rgba(255,255,255,0.48), inset -${shift}px 0 ${shift * 2}px rgba(255,69,58,0.34), inset ${shift}px 0 ${shift * 2}px rgba(10,132,255,0.4)`;
    }
  }

  iorSlider?.addEventListener("input", updateOpticalLens);
  dispSlider?.addEventListener("input", updateOpticalLens);

  opticalPlayground?.addEventListener("pointermove", (e) => {
    if (reducedMotionQuery.matches || !opticalLens) return;
    const rect = opticalPlayground.getBoundingClientRect();
    const dx = ((e.clientX - rect.left) / rect.width - 0.5) * 18;
    const dy = ((e.clientY - rect.top) / rect.height - 0.5) * 12;
    opticalLens.style.transform = `translate(${dx.toFixed(1)}px, ${dy.toFixed(1)}px)`;
  });
  opticalPlayground?.addEventListener("pointerleave", () => {
    if (opticalLens) opticalLens.style.transform = "translate(0px, 0px)";
  });
  updateOpticalLens();

  // =========================================================================
  // 7. Global Hotkeys Matrix + Interactive Feedback
  // =========================================================================
  const HOTKEYS = [
    ["Ctrl + Alt + Space", "Play / Pause active media session"],
    ["Ctrl + Alt + I", "Toggle Expand / Collapse Island"],
    ["Ctrl + Alt + A", "Start / Pause Stopwatch or Pomodoro"],
    ["Ctrl + Alt + V", "Open Clipboard History"],
    ["Ctrl + Alt + C", "Open Quick Calculator & Converter"],
    ["Ctrl + Alt + O", "Open Audio Output Switcher"],
    ["Ctrl + Alt + P", "Open Battery & Power Mode"],
    ["Ctrl + Alt + H", "Open System Performance HUD"],
    ["Ctrl + Alt + R", "Start / Stop Voice Memo"],
    ["Ctrl + Alt + D", "Connected Peripherals Battery Hub"],
    ["Ctrl + Alt + W", "Open Wi-Fi & Network Hub"],
    ["Ctrl + Alt + N", "Notification History Center"],
    ["Ctrl + Alt + S", "Appearance & Settings"],
    ["Ctrl + Alt + M", "Switch Target Monitor"],
    ["Ctrl + Alt + Q", "Exit Dynamic Island"]
  ];

  const hotkeysGrid = document.getElementById("hotkeys-grid");
  const hotkeyFeedback = document.getElementById("hotkey-feedback");
  if (hotkeysGrid) {
    hotkeysGrid.innerHTML = HOTKEYS.map(
      ([keys, action]) => `
      <button class="hotkey-item" type="button" data-hotkey="${keys}" data-action="${action}">
        <span class="hotkey-action">${action}</span>
        <kbd>${keys}</kbd>
      </button>`
    ).join("");

    hotkeysGrid.addEventListener("click", (e) => {
      const item = e.target.closest("[data-hotkey]");
      if (!item) return;
      if (hotkeyFeedback) {
        hotkeyFeedback.textContent = `Simulated ${item.dataset.hotkey} → ${item.dataset.action}`;
      }
      playHaptic("tick");
    });
  }

  // =========================================================================
  // 8. Pointer-Reactive Card Spotlight, Scroll Reveal, Theme & Copy Helpers
  // =========================================================================
  if (!reducedMotionQuery.matches) {
    document.querySelectorAll(".glass-card").forEach((card) => {
      card.addEventListener("pointermove", (e) => {
        const r = card.getBoundingClientRect();
        card.style.setProperty("--mx", `${e.clientX - r.left}px`);
        card.style.setProperty("--my", `${e.clientY - r.top}px`);
      });
    });

    const reveals = document.querySelectorAll(".reveal");
    if ("IntersectionObserver" in window) {
      reveals.forEach((el) => el.classList.add("pending"));
      const io = new IntersectionObserver(
        (entries) => {
          entries.forEach((entry) => {
            if (entry.isIntersecting) {
              entry.target.classList.remove("pending");
              entry.target.classList.add("visible");
              io.unobserve(entry.target);
            }
          });
        },
        { threshold: 0.1 }
      );
      reveals.forEach((el) => io.observe(el));
    }
  }

  const themeToggleBtn = document.getElementById("theme-toggle");
  const rootEl = document.documentElement;
  const savedTheme = localStorage.getItem("di-site-theme");
  if (savedTheme === "light" || savedTheme === "dark") {
    rootEl.setAttribute("data-theme", savedTheme);
  }

  function syncThemeButton() {
    const isLight = rootEl.getAttribute("data-theme") === "light";
    if (themeToggleBtn) {
      themeToggleBtn.setAttribute("aria-pressed", String(isLight));
      themeToggleBtn.setAttribute("aria-label", isLight ? "Switch to dark mode" : "Switch to light mode");
    }
  }

  if (themeToggleBtn) {
    syncThemeButton();
    themeToggleBtn.addEventListener("click", () => {
      const next = rootEl.getAttribute("data-theme") === "light" ? "dark" : "light";
      rootEl.setAttribute("data-theme", next);
      localStorage.setItem("di-site-theme", next);
      syncThemeButton();
      playHaptic("tick");
    });
  }

  const mobileMenuBtn = document.getElementById("mobile-menu-btn");
  const primaryNav = document.getElementById("primary-nav");
  if (mobileMenuBtn && primaryNav) {
    mobileMenuBtn.addEventListener("click", () => {
      const open = primaryNav.classList.toggle("open");
      mobileMenuBtn.setAttribute("aria-expanded", String(open));
    });
    primaryNav.querySelectorAll("a").forEach((link) => {
      link.addEventListener("click", () => {
        primaryNav.classList.remove("open");
        mobileMenuBtn.setAttribute("aria-expanded", "false");
      });
    });
  }

  document.querySelectorAll("[data-copy]").forEach((btn) => {
    btn.addEventListener("click", async () => {
      const text = btn.getAttribute("data-copy");
      if (!text) return;
      const original = btn.textContent;
      try {
        await navigator.clipboard.writeText(text);
        btn.textContent = "Copied!";
        playHaptic("snap");
      } catch {
        btn.textContent = "Copy failed";
      }
      setTimeout(() => {
        btn.textContent = original;
      }, 1600);
    });
  });

  window.addEventListener("beforeunload", () => {
    clearInterval(timerInterval);
  });
})();
