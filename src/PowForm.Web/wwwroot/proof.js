// The client side of the proof of work: fetch a challenge, find its number in a worker, and
// return the answer, "number:signature".

/**
 * Gets an answer for a purpose: fetches a challenge and solves it.
 * @param {string} purpose "contact" or "newsletter"
 * @returns {Promise<string>} "number:signature"
 */
export async function solve(purpose) {
  // crypto.subtle exists only in a secure context: HTTPS, or http://localhost. On plain HTTP
  // elsewhere (a LAN address) it is undefined, in the page and in workers, and nothing here can run.
  if (!globalThis.crypto?.subtle) {
    throw new Error(
      "The proof of work needs a secure context (HTTPS or localhost).",
    );
  }

  const response = await fetch(
    `/challenge?for=${encodeURIComponent(purpose)}`,
    {
      headers: { Accept: "application/json" },
      cache: "no-store",
    },
  );
  if (!response.ok) {
    throw new Error(`No challenge (${response.status})`);
  }

  const challenge = await response.json();
  const number = await work(challenge);
  if (number === null) {
    throw new Error("No answer");
  }

  return `${number}:${challenge.signature}`;
}

/**
 * Finds the number in a worker, or on this thread where workers can't be used.
 * @param {{ salt: string, challenge: string, maxNumber: number }} challenge
 * @param {number} [batch] hashes per batch; the worker's default when left out
 * @returns {Promise<number | null>}
 */
export function work({ salt, challenge, maxNumber }, batch) {
  if (typeof Worker === "undefined") {
    return solveHere(salt, challenge, maxNumber, batch ?? 256);
  }

  return new Promise((resolve) => {
    let worker;
    try {
      worker = new Worker(new URL("./proof-worker.js", import.meta.url));
    } catch {
      resolve(solveHere(salt, challenge, maxNumber, batch ?? 256));
      return;
    }

    worker.addEventListener("message", (event) => {
      worker.terminate();
      resolve(event.data?.number ?? null);
    });
    // A worker that fails to start (blocked by a policy, say) falls back to this thread.
    worker.addEventListener("error", () => {
      worker.terminate();
      resolve(solveHere(salt, challenge, maxNumber, batch ?? 256));
    });
    worker.postMessage({ salt, challenge, maxNumber, batch });
  });
}

// The same search as the worker, on the main thread. It still uses crypto.subtle, so it doesn't
// help where that is missing; it only covers a missing or failing Worker.
async function solveHere(salt, challenge, maxNumber, batch) {
  const encoder = new TextEncoder();
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
      if (hex(results[i]) === challenge) {
        return start + i;
      }
    }
  }

  return null;
}

function hex(buffer) {
  return Array.from(new Uint8Array(buffer), (byte) =>
    byte.toString(16).padStart(2, "0"),
  ).join("");
}
