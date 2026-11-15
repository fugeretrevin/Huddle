"""
Procedural studio audio cue synthesizer for MuseDesk (Art / Audio layer).

Generates 12 restrained, tactile mono 16-bit 22,050 Hz PCM .wav cues under
Assets/MuseDesk/Art/Audio/ with smooth fade-in/out envelopes and peaks
normalized to approximately -6 dBFS.

Uses Python standard library only (wave, math, struct, os).
"""

import math
import os
import struct
import wave

SAMPLE_RATE = 22050
TARGET_PEAK_DBFS = -6.0
TARGET_PEAK_LINEAR = 10.0 ** (TARGET_PEAK_DBFS / 20.0)  # ~0.501187


class DeterministicNoise:
    """Tiny deterministic LCG pseudo-random generator in [-1.0, 1.0]."""

    def __init__(self, seed: int = 0x517CC1B7):
        self.state = seed & 0xFFFFFFFF

    def next_float(self) -> float:
        self.state = (1664525 * self.state + 1013904223) & 0xFFFFFFFF
        return (self.state / 2147483647.5) - 1.0


def hann_fade(samples: list[float], fade_in_ms: float = 5.0, fade_out_ms: float = 12.0) -> list[float]:
    n = len(samples)
    if n == 0:
        return samples
    fi = min(n // 2, max(1, int(SAMPLE_RATE * fade_in_ms / 1000.0)))
    fo = min(n // 2, max(1, int(SAMPLE_RATE * fade_out_ms / 1000.0)))

    for i in range(fi):
        w = 0.5 * (1.0 - math.cos(math.pi * i / fi))
        samples[i] *= w
    for i in range(fo):
        w = 0.5 * (1.0 - math.cos(math.pi * i / fo))
        samples[n - 1 - i] *= w
    return samples


def one_pole_lowpass(samples: list[float], cutoff_hz: float) -> list[float]:
    if not samples:
        return samples
    rc = 1.0 / (2.0 * math.pi * max(20.0, cutoff_hz))
    dt = 1.0 / SAMPLE_RATE
    alpha = dt / (rc + dt)
    out = [0.0] * len(samples)
    y = 0.0
    for i, x in enumerate(samples):
        y = y + alpha * (x - y)
        out[i] = y
    return out


def one_pole_highpass(samples: list[float], cutoff_hz: float) -> list[float]:
    if not samples:
        return samples
    rc = 1.0 / (2.0 * math.pi * max(20.0, cutoff_hz))
    dt = 1.0 / SAMPLE_RATE
    alpha = rc / (rc + dt)
    out = [0.0] * len(samples)
    y = 0.0
    prev_x = 0.0
    for i, x in enumerate(samples):
        y = alpha * (y + x - prev_x)
        prev_x = x
        out[i] = y
    return out


def remove_dc_and_normalize(samples: list[float], target_peak: float = TARGET_PEAK_LINEAR) -> list[float]:
    if not samples:
        return samples
    mean = sum(samples) / len(samples)
    centered = [s - mean for s in samples]
    # Re-apply brief 3 ms edge taper after DC removal so start/end hit exact 0.0
    hann_fade(centered, fade_in_ms=3.0, fade_out_ms=6.0)
    peak = max(abs(s) for s in centered)
    if peak < 1e-9:
        return [0.0] * len(centered)
    scale = target_peak / peak
    return [max(-0.99, min(0.99, s * scale)) for s in centered]


def write_wav_mono_16(path: str, samples: list[float]) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    processed = remove_dc_and_normalize(samples)
    pcm = bytearray()
    for s in processed:
        val = int(round(s * 32767.0))
        val = max(-32767, min(32767, val))
        pcm.extend(struct.pack("<h", val))

    with wave.open(path, "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(SAMPLE_RATE)
        wf.writeframes(bytes(pcm))


def make_buffer(duration_ms: float) -> list[float]:
    return [0.0] * max(1, int(round(SAMPLE_RATE * duration_ms / 1000.0)))


def add_tone_burst(
    buf: list[float],
    start_ms: float,
    dur_ms: float,
    freq_start: float,
    freq_end: float,
    amp: float,
    attack_ms: float = 4.0,
    decay_tau_ms: float = 60.0,
    harmonic2: float = 0.15,
    harmonic3: float = 0.04,
) -> None:
    i0 = max(0, int(round(SAMPLE_RATE * start_ms / 1000.0)))
    count = int(round(SAMPLE_RATE * dur_ms / 1000.0))
    att = max(1, int(round(SAMPLE_RATE * attack_ms / 1000.0)))
    tau = max(0.001, decay_tau_ms / 1000.0)
    phase = 0.0
    for k in range(count):
        idx = i0 + k
        if idx >= len(buf):
            break
        u = k / max(1, count - 1)
        freq = freq_start + (freq_end - freq_start) * u
        phase += 2.0 * math.pi * freq / SAMPLE_RATE
        t_sec = k / SAMPLE_RATE
        env = math.exp(-t_sec / tau)
        if k < att:
            env *= 0.5 * (1.0 - math.cos(math.pi * k / att))
        # Smooth tail ramp in last 15% of burst
        if u > 0.85:
            tail = (1.0 - u) / 0.15
            env *= 0.5 * (1.0 - math.cos(math.pi * tail))
        sig = (
            math.sin(phase)
            + harmonic2 * math.sin(2.0 * phase)
            + harmonic3 * math.sin(3.0 * phase)
        )
        buf[idx] += amp * env * sig


def synth_button_press() -> list[float]:
    # Tactile ceramic/wooden key click (~90 ms)
    buf = make_buffer(90.0)
    rng = DeterministicNoise(101)
    n = len(buf)
    noise = [rng.next_float() * math.exp(-(i / SAMPLE_RATE) / 0.009) for i in range(n)]
    noise = one_pole_lowpass(one_pole_highpass(noise, 700.0), 3400.0)
    for i in range(n):
        buf[i] += 0.35 * noise[i]
    add_tone_burst(buf, 0.0, 65.0, 920.0, 420.0, amp=0.55, attack_ms=2.0, decay_tau_ms=14.0, harmonic2=0.12, harmonic3=0.0)
    add_tone_burst(buf, 12.0, 60.0, 560.0, 380.0, amp=0.30, attack_ms=3.0, decay_tau_ms=18.0, harmonic2=0.05, harmonic3=0.0)
    return hann_fade(buf, 2.0, 10.0)


def synth_paper_pickup() -> list[float]:
    # Gentle parchment/cardstock lift rustle + soft upward pitch hint (~145 ms)
    buf = make_buffer(145.0)
    rng = DeterministicNoise(202)
    n = len(buf)
    raw = []
    for i in range(n):
        u = i / max(1, n - 1)
        # Two soft friction swells
        env = math.sin(math.pi * u) ** 1.3 * (0.7 + 0.3 * math.sin(2.0 * math.pi * u * 2.5))
        raw.append(rng.next_float() * env)
    filtered = one_pole_highpass(one_pole_lowpass(raw, 4200.0), 1100.0)
    for i in range(n):
        buf[i] += 0.75 * filtered[i]
    add_tone_burst(buf, 15.0, 100.0, 440.0, 620.0, amp=0.14, attack_ms=14.0, decay_tau_ms=42.0, harmonic2=0.05, harmonic3=0.0)
    return hann_fade(buf, 8.0, 18.0)


def synth_stamp_press() -> list[float]:
    # Solid wooden-rubber stamp thud + subtle crisp latch (~175 ms)
    buf = make_buffer(175.0)
    rng = DeterministicNoise(303)
    n = len(buf)
    thump_noise = [rng.next_float() * math.exp(-(i / SAMPLE_RATE) / 0.016) for i in range(n)]
    thump_noise = one_pole_lowpass(thump_noise, 1400.0)
    for i in range(n):
        buf[i] += 0.40 * thump_noise[i]
    add_tone_burst(buf, 0.0, 140.0, 195.0, 88.0, amp=0.80, attack_ms=3.0, decay_tau_ms=34.0, harmonic2=0.22, harmonic3=0.06)
    add_tone_burst(buf, 22.0, 80.0, 680.0, 520.0, amp=0.20, attack_ms=3.0, decay_tau_ms=22.0, harmonic2=0.08, harmonic3=0.0)
    return hann_fade(buf, 3.0, 20.0)


def synth_task_assign() -> list[float]:
    # Warm two-note studio chime G4 -> B4 (~250 ms)
    buf = make_buffer(250.0)
    add_tone_burst(buf, 0.0, 140.0, 392.0, 392.0, amp=0.55, attack_ms=6.0, decay_tau_ms=55.0, harmonic2=0.12, harmonic3=0.03)
    add_tone_burst(buf, 85.0, 160.0, 493.88, 493.88, amp=0.65, attack_ms=6.0, decay_tau_ms=68.0, harmonic2=0.10, harmonic3=0.02)
    return hann_fade(buf, 4.0, 25.0)


def synth_elevator_arrival() -> list[float]:
    # Warm brass treehouse elevator ding: C5 + E5/G5 soft bell shimmer (~350 ms)
    buf = make_buffer(350.0)
    add_tone_burst(buf, 0.0, 220.0, 523.25, 523.25, amp=0.50, attack_ms=4.0, decay_tau_ms=95.0, harmonic2=0.14, harmonic3=0.04)
    add_tone_burst(buf, 95.0, 250.0, 783.99, 783.99, amp=0.62, attack_ms=4.0, decay_tau_ms=115.0, harmonic2=0.08, harmonic3=0.02)
    return hann_fade(buf, 4.0, 35.0)


def synth_manager_printer() -> list[float]:
    # Miniature carriage zip + soft paper feed ticks (~300 ms)
    buf = make_buffer(300.0)
    rng = DeterministicNoise(606)
    n = len(buf)
    # 3 soft mechanical carriage clicks + brief paper slide
    for t_ms, f0 in [(10.0, 760.0), (75.0, 880.0), (140.0, 810.0)]:
        add_tone_burst(buf, t_ms, 45.0, f0, f0 * 0.72, amp=0.45, attack_ms=2.0, decay_tau_ms=14.0, harmonic2=0.18, harmonic3=0.05)
    slide = []
    for i in range(n):
        t_ms = 1000.0 * i / SAMPLE_RATE
        env = math.exp(-((t_ms - 210.0) ** 2) / (2.0 * 32.0 ** 2))
        slide.append(rng.next_float() * env)
    slide = one_pole_highpass(one_pole_lowpass(slide, 3600.0), 900.0)
    for i in range(n):
        buf[i] += 0.42 * slide[i]
    add_tone_burst(buf, 185.0, 100.0, 520.0, 660.0, amp=0.22, attack_ms=8.0, decay_tau_ms=38.0, harmonic2=0.06, harmonic3=0.0)
    return hann_fade(buf, 4.0, 22.0)


def synth_courier_depart() -> list[float]:
    # Gentle airy upward lift + soft chime (~260 ms)
    buf = make_buffer(260.0)
    rng = DeterministicNoise(707)
    n = len(buf)
    air = []
    for i in range(n):
        u = i / max(1, n - 1)
        env = math.sin(math.pi * u) * math.exp(-u * 1.1)
        air.append(rng.next_float() * env)
    air = one_pole_highpass(one_pole_lowpass(air, 2600.0), 500.0)
    for i in range(n):
        buf[i] += 0.32 * air[i]
    add_tone_burst(buf, 10.0, 220.0, 330.0, 587.33, amp=0.58, attack_ms=14.0, decay_tau_ms=88.0, harmonic2=0.10, harmonic3=0.02)
    return hann_fade(buf, 6.0, 26.0)


def synth_courier_arrive() -> list[float]:
    # Soft descending air cushion settle + warm ping (~280 ms)
    buf = make_buffer(280.0)
    rng = DeterministicNoise(808)
    n = len(buf)
    air = []
    for i in range(n):
        u = i / max(1, n - 1)
        env = math.sin(math.pi * u) * (1.0 - 0.35 * u)
        air.append(rng.next_float() * env)
    air = one_pole_lowpass(air, 1900.0)
    for i in range(n):
        buf[i] += 0.28 * air[i]
    add_tone_burst(buf, 15.0, 140.0, 523.25, 349.23, amp=0.38, attack_ms=10.0, decay_tau_ms=55.0, harmonic2=0.08, harmonic3=0.0)
    add_tone_burst(buf, 80.0, 190.0, 587.33, 587.33, amp=0.55, attack_ms=5.0, decay_tau_ms=72.0, harmonic2=0.10, harmonic3=0.02)
    return hann_fade(buf, 6.0, 28.0)


def synth_portal_open() -> list[float]:
    # Restrained ascending harmonic shimmer (~380 ms)
    buf = make_buffer(380.0)
    rng = DeterministicNoise(909)
    n = len(buf)
    breath = [rng.next_float() * math.sin(math.pi * i / max(1, n - 1)) for i in range(n)]
    breath = one_pole_highpass(one_pole_lowpass(breath, 2400.0), 650.0)
    for i in range(n):
        buf[i] += 0.16 * breath[i]
    add_tone_burst(buf, 0.0, 340.0, 293.66, 440.0, amp=0.45, attack_ms=26.0, decay_tau_ms=145.0, harmonic2=0.14, harmonic3=0.04)
    add_tone_burst(buf, 55.0, 310.0, 440.0, 659.25, amp=0.42, attack_ms=28.0, decay_tau_ms=135.0, harmonic2=0.08, harmonic3=0.02)
    return hann_fade(buf, 10.0, 40.0)


def synth_portal_close() -> list[float]:
    # Restrained descending harmonic fold-down (~320 ms)
    buf = make_buffer(320.0)
    add_tone_burst(buf, 0.0, 290.0, 587.33, 329.63, amp=0.48, attack_ms=14.0, decay_tau_ms=115.0, harmonic2=0.10, harmonic3=0.02)
    add_tone_burst(buf, 35.0, 260.0, 392.0, 246.94, amp=0.40, attack_ms=16.0, decay_tau_ms=105.0, harmonic2=0.08, harmonic3=0.0)
    return hann_fade(buf, 8.0, 36.0)


def synth_project_complete() -> list[float]:
    # Cozy 3-note ascending studio chord arpeggio C5 - E5 - G5 (~460 ms)
    buf = make_buffer(460.0)
    add_tone_burst(buf, 0.0, 240.0, 523.25, 523.25, amp=0.44, attack_ms=6.0, decay_tau_ms=105.0, harmonic2=0.10, harmonic3=0.02)
    add_tone_burst(buf, 95.0, 260.0, 659.25, 659.25, amp=0.48, attack_ms=6.0, decay_tau_ms=115.0, harmonic2=0.09, harmonic3=0.02)
    add_tone_burst(buf, 190.0, 265.0, 783.99, 783.99, amp=0.58, attack_ms=6.0, decay_tau_ms=130.0, harmonic2=0.08, harmonic3=0.02)
    return hann_fade(buf, 5.0, 45.0)


def synth_task_failure() -> list[float]:
    # Gentle, non-harsh muted two-note downward wooden nudge E4 -> C4 (~300 ms)
    buf = make_buffer(300.0)
    add_tone_burst(buf, 0.0, 145.0, 329.63, 315.0, amp=0.52, attack_ms=8.0, decay_tau_ms=55.0, harmonic2=0.06, harmonic3=0.0)
    add_tone_burst(buf, 105.0, 185.0, 261.63, 248.0, amp=0.56, attack_ms=8.0, decay_tau_ms=72.0, harmonic2=0.05, harmonic3=0.0)
    buf = one_pole_lowpass(buf, 1600.0)
    return hann_fade(buf, 6.0, 30.0)


CUES = {
    "SFX_ButtonPress.wav": synth_button_press,
    "SFX_PaperPickup.wav": synth_paper_pickup,
    "SFX_StampPress.wav": synth_stamp_press,
    "SFX_TaskAssign.wav": synth_task_assign,
    "SFX_ElevatorArrival.wav": synth_elevator_arrival,
    "SFX_ManagerPrinter.wav": synth_manager_printer,
    "SFX_CourierDepart.wav": synth_courier_depart,
    "SFX_CourierArrive.wav": synth_courier_arrive,
    "SFX_PortalOpen.wav": synth_portal_open,
    "SFX_PortalClose.wav": synth_portal_close,
    "SFX_ProjectComplete.wav": synth_project_complete,
    "SFX_TaskFailure.wav": synth_task_failure,
}


def main() -> None:
    scripts_dir = os.path.dirname(os.path.abspath(__file__))
    repo_root = os.path.abspath(os.path.join(scripts_dir, "..", ".."))
    out_dir = os.path.join(repo_root, "Assets", "MuseDesk", "Art", "Audio")
    os.makedirs(out_dir, exist_ok=True)

    for filename, fn in CUES.items():
        samples = fn()
        out_path = os.path.join(out_dir, filename)
        write_wav_mono_16(out_path, samples)
        dur_ms = 1000.0 * len(samples) / SAMPLE_RATE
        print(f"Generated {filename}: {dur_ms:.1f} ms, {len(samples)} samples @ {SAMPLE_RATE} Hz")


if __name__ == "__main__":
    main()
