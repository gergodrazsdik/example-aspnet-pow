// The client side of the proof of work: fetch a challenge, find its number in a worker, and
// return the answer, "number:signature". guard() at the end wires it to a form.

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

// A challenge lives 10 minutes (ProofOfWork.Lifetime); an answer older than 9 is stale, so a form
// left open over lunch solves again on submit instead of sending an answer the server will refuse.
const FRESH_FOR = 9 * 60 * 1000;

/**
 * Guards a form. Solving starts when the visitor first focuses or types in the form; the answer
 * goes in the form's hidden `proof` field. If they send before it is ready, the submit waits
 * (saying so in the form's [data-proof-status], from its data-waiting text) and goes on when the
 * answer is there. A failed solve is not kept: the next submit solves again. If there is still no
 * answer (no secure context, no network), the form goes without one and the server decides.
 * @param {HTMLFormElement} form
 * @param {string} purpose
 */
export function guard(form, purpose) {
  const field = form.elements.namedItem("proof");
  const status = form.querySelector("[data-proof-status]");
  if (!(field instanceof HTMLInputElement)) {
    return;
  }

  // Back or Forward can show this form again with the answer that was just spent in the field
  // (the browser restores form state, or the page comes whole from the back/forward cache).
  // An answer works once, so never reuse one on such a page.
  const reloadedByHistory =
    performance.getEntriesByType("navigation")[0]?.type === "back_forward";
  if (reloadedByHistory) {
    field.value = "";
  }

  // The server sends back an answer it did not use (a post refused for its text): keep it.
  let current = field.value
    ? { answer: Promise.resolve(field.value), startedAt: Date.now() }
    : null;

  const start = () => {
    if (!current || Date.now() - current.startedAt > FRESH_FOR) {
      field.value = "";
      const pending = { startedAt: Date.now(), answer: null };
      pending.answer = solve(purpose).then(
        (value) => {
          field.value = value;
          return value;
        },
        (error) => {
          console.warn(error);
          // Don't keep the failure: the next start() tries again.
          if (current === pending) {
            current = null;
          }

          return null;
        },
      );
      current = pending;
    }

    return current.answer;
  };

  // Start on the first sign of life. Registering the same listener again is a no-op while it is still
  // registered, so arm() can be called whenever the page may need to start over.
  const arm = () => {
    form.addEventListener("focusin", start, { once: true });
    form.addEventListener("input", start, { once: true });
  };
  arm();

  // A submission is under way: a second click or Enter must not send the same answer again (the
  // first post is saved, the second would be refused as "already used", and the visitor would
  // resend and get the message twice). Cleared after a few seconds in case the request failed.
  let submitted = false;
  const markSubmitted = () => {
    submitted = true;
    setTimeout(() => {
      submitted = false;
    }, 5000);
  };

  window.addEventListener("pageshow", (event) => {
    if (event.persisted) {
      // Restored from the back/forward cache: the answer in the field was spent.
      current = null;
      field.value = "";
      submitted = false;
      arm(); // the listeners were used up before the page was cached; typing must start the solver again
    }
  });

  let waiting = false;
  let sending = false;
  form.addEventListener("submit", async (event) => {
    if (sending) {
      markSubmitted();
      return;
    }

    if (submitted) {
      event.preventDefault();
      return;
    }

    const fresh = current && Date.now() - current.startedAt <= FRESH_FOR;
    if (field.value && fresh) {
      markSubmitted();
      return;
    }

    event.preventDefault();
    if (waiting) {
      return;
    }

    waiting = true;
    if (status) {
      status.textContent = status.dataset.waiting ?? "";
    }

    await start();
    // Leave this submit event's dispatch first. When the solve fails at once (no crypto.subtle),
    // the code after the await runs in a microtask, still inside the dispatch, where requestSubmit()
    // silently does nothing (the form is still "firing submission events").
    await new Promise((resolve) => setTimeout(resolve));
    if (status) {
      status.textContent = "";
    }

    waiting = false;
    sending = true;
    form.requestSubmit(
      event.submitter instanceof HTMLElement ? event.submitter : undefined,
    );
    sending = false;
  });
}
