// Mutfak zili ve garson bildirimi için kısa sesler. Ses dosyası yok; tarayıcının ses motoru (Web Audio)
// ile üretiliyor. Tarayıcılar kullanıcı sayfaya dokunmadan ses çalmaya izin vermez; bu yüzden
// enableSound() bir düğme tıklamasıyla çağrılmalı.

let audio: AudioContext | null = null

export function enableSound() {
  audio ??= new AudioContext()
  void audio.resume()
  playBell()
}

/**
 * Sesi çalmadan etkinleştirir. main.tsx bunu sayfadaki ilk dokunuşta çağırır; böylece garson
 * PIN'ini girerken ses kendiliğinden açılmış olur.
 */
export function unlockSound() {
  audio ??= new AudioContext()
  void audio.resume()
}

export function isSoundEnabled() {
  return audio?.state === 'running'
}

/** Mutfak zili: iki notalı "ding-dong". */
export function playBell() {
  tone(880, 0, 0.6)
  tone(660, 0.25, 0.9)
}

/** Garson bildirimi: kısa, yumuşak tek nota. */
export function playChime() {
  tone(1046, 0, 0.35)
}

function tone(frequency: number, delay: number, duration: number) {
  if (!audio || audio.state !== 'running') return
  const start = audio.currentTime + delay
  const oscillator = audio.createOscillator()
  const gain = audio.createGain()
  oscillator.type = 'sine'
  oscillator.frequency.value = frequency
  // Hızlı yükselip yavaşça sönen ses; "tık" sesi çıkmasın diye sıfırdan başlıyor.
  gain.gain.setValueAtTime(0.0001, start)
  gain.gain.exponentialRampToValueAtTime(0.4, start + 0.02)
  gain.gain.exponentialRampToValueAtTime(0.0001, start + duration)
  oscillator.connect(gain).connect(audio.destination)
  oscillator.start(start)
  oscillator.stop(start + duration)
}
