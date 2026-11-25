// The solver. It runs in a Web Worker so typing never stutters: it finds the number whose
// SHA-256, after the salt, equals the challenge. A plain worker (no import), so the CSP only
// needs worker-src 'self'.
//
// Each crypto.subtle.digest call has a fixed cost (it is asynchronous, and crosses to the
// browser's crypto code). Starting a batch of them and awaiting them together shares that cost.
// Measured in Chromium, batches of 16 or more were about 12 percent faster than one at a time,
// with no further gain at 1,024; the benefit differs between machines and engines.

const DEFAULT_BATCH = 256;

self.addEventListener("message", async (event) => {
  const { salt, challenge, maxNumber, batch } = event.data ?? {};
  try {
    self.postMessage({
      number: await solve(salt, challenge, maxNumber, batch ?? DEFAULT_BATCH),
    });
  } catch (error) {
    self.postMessage({ number: null, error: String(error) });
  }
});

/**
 * Finds the number, or null when none from 0 to maxNumber matches.
 * @param {string} salt
 * @param {string} challenge lowercase hex
 * @param {number} maxNumber
 * @param {number} batch how many hashes to start before awaiting them
 * @returns {Promise<number | null>}
 */
async function solve(salt, challenge, maxNumber, batch) {
  const encoder = new TextEncoder();
  const target = Uint8Array.from(challenge.match(/../g), (pair) =>
    parseInt(pair, 16),
  );
  for (let start = 0; start <= maxNumber; start += batch) {
    const end = Math.min(start + batch - 1, maxNumber);
    const digests = [];
    for (let number = start; number <= end; number++) {
      digests.push(
        crypto.subtle.digest("SHA-256", encoder.encode(salt + number)),
      );
    }

    const results = await Promise.all(digests);
    for (let i = 0; i < results.length; i++) {
      if (same(new Uint8Array(results[i]), target)) {
        return start + i;
      }
    }
  }

  return null;
}

function same(a, b) {
  return a.length === b.length && a.every((byte, i) => byte === b[i]);
}
