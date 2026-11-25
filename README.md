# example-aspnet-pow

An invisible proof of work for a contact form in ASP.NET Core (Razor Pages): the example for the article
"Spam protection without a CAPTCHA: an invisible proof of work in ASP.NET Core".

The server hands out a **challenge**: a salt, and the SHA-256 of that salt followed by a secret
number. The browser finds the number by trying 0, 1, 2, ... in a Web Worker while the visitor types,
and sends it back with the form. The visitor sees and solves nothing.

- **Stateless to issue.** The challenge is signed with ASP.NET Core Data Protection, so any replica
  can check an answer. The salt carries the form's purpose and an expiry, inside the signed data.
- **Once only.** A small store remembers used challenges until they expire.
- **No JavaScript?** The form still posts; this example has no other defences, so the server refuses
  the post and says why.
- Inspired by [ALTCHA](https://altcha.org), written from scratch: no ALTCHA library or widget,
  no NuGet package for the proof of work, no build step for the JavaScript.

This is a companion to an article, not a maintained project.

## Quick start

Needs the .NET 11 SDK. It also builds and passes its tests on .NET 10 (checked with SDK 10.0.404):
change `TargetFramework` in `Directory.Build.props` to `net10.0` and the SDK in `global.json` to `10.0.100`.

```sh
dotnet run --project src/PowForm.Web
```

Open <http://localhost:5200>, type a message and send it. The proof is solved while you type. Then
try the bot:

```sh
scripts/flood.sh          # 100 posts with curl, no answer: every one is refused (HTTP 403)
```

`crypto.subtle` only exists in a secure context (HTTPS or `localhost`). Open the form over plain HTTP
at a LAN address and the browser can't compute the proof; the form then posts without one and is
refused with a message that says so.

## The code

| File | What it does |
|---|---|
| `src/PowForm.Web/ProofOfWork.cs` | `Create(purpose)` and `VerifyAsync(purpose, answer)`: the whole server side |
| `src/PowForm.Web/UsedChallenges.cs` | `IUsedChallenges` and the in-memory store |
| `src/PowForm.Web/Pages/Contact.cshtml(.cs)` | the contact form (Razor Pages): fields first, then the proof, then save |
| `src/PowForm.Web/Pages/Newsletter.cshtml(.cs)` | a second form with its own purpose |
| `src/PowForm.Web/Pages/Shared/_Proof.cshtml` | the hidden `proof` field, the status line, the no-JavaScript note |
| `src/PowForm.Web/ProofProblems.cs` | the check and the messages the visitor sees |
| `src/PowForm.Web/wwwroot/proof-worker.js` | the solver, in a worker |
| `src/PowForm.Web/wwwroot/proof.js` | fetch a challenge, run the worker, `guard(form, purpose)` |
| `src/PowForm.Web/Program.cs` | Data Protection, the options, `GET /challenge`, the strict CSP |

A challenge, as `GET /challenge?for=contact` sends it:

```json
{
  "algorithm": "SHA-256",
  "challenge": "6a4d...a85",
  "salt": "contact.1795620192.d2b5f217b94b250f685511d6",
  "maxNumber": 100000,
  "signature": "CfDJ8KaWLoQE0VpBtd4Ia7Yps..."
}
```

The salt is `purpose.expiryUnixSeconds.randomHex`; `challenge` is the SHA-256 hex of the salt
followed by the secret number (0 to `maxNumber`); the signature is Data Protection over
`challenge\nsalt`. The answer is `number:signature`.

Verifying (`ProofOfWork.VerifyAsync`) parses `number:signature`, unprotects the signature (a
`CryptographicException` means refused), checks the purpose and the expiry from the **signed** salt,
recomputes the hash and compares it with `CryptographicOperations.FixedTimeEquals`, and last marks
the challenge used. The contact page (`POST /contact`) validates the fields first and verifies the answer last, so a post
refused for its text keeps its answer.

## Difficulty

`Proof:MaxNumber` in `appsettings.json` (default 100,000, clamped to 1 to 10,000,000). The secret
number is uniform in `0..MaxNumber`, so the work is random: on average `MaxNumber / 2` hashes, and
the spread is wide. Override it with the environment variable `Proof__MaxNumber`.

## Scaling out

Two things are per instance in this example and must be shared across replicas:

1. **Data Protection keys.** They live in `src/PowForm.Web/App_Data/keys` (ignored by git) and the
   application name is fixed (`PowForm`). Replicas need the same keys and the same name, for example
   `PersistKeysToAzureBlobStorage`, `PersistKeysToStackExchangeRedis` or a shared volume.
2. **The used-challenge store.** Without a shared store, an answer used on replica A is fresh on
   replica B. Implement the interface over a shared table, Redis or a database row with a unique key:

   ```csharp
   public interface IUsedChallenges
   {
       // True the first time, false when the challenge was used before.
       Task<bool> TryUseAsync(string challenge, DateTimeOffset expires, CancellationToken cancellationToken = default);
   }
   ```

   `expires` says when the entry can be deleted. A unique-key insert (`INSERT` and catch the
   duplicate, or `SET NX` with a TTL) is the atomic "first time".

## Tests

The browser tests need a Playwright browser, so install it first (the build produces the script):

```sh
dotnet build
pwsh artifacts/bin/PowForm.Screens/debug/playwright.ps1 install chromium   # add firefox if you like
dotnet test                       # unit, endpoint and browser tests (outside a sandbox)
```

No browser, or only want the logic? `dotnet test --project tests/PowForm.Tests` runs the unit and endpoint tests without one.

- `tests/PowForm.Tests`: TUnit. `ProofOfWork` (shape, verification, replay, expiry, the scaling-out failure
  mode) and the endpoints in memory with `TestWebApplicationFactory`.
- `tests/PowForm.Screens`: TUnit with Playwright. It starts the app as a process and drives it in
  Chromium (and Firefox and WebKit where installed): the worker and the module load under the strict CSP
  with no violations, the form's answer satisfies the server, a failed challenge recovers on the next
  send, a stale answer is solved again, a double click posts once, Back doesn't reuse a spent answer,
  no `crypto.subtle` gives a clear refusal. It also saves screenshots to `artifacts/screenshots`
  (ignored by git; set `POW_OUTPUT` to write elsewhere).

The timings come from tests that run only on request (they take minutes and depend on the machine);
they write CSV files to `artifacts/measurements`:

```sh
POW_MEASURE=1 dotnet test --project tests/PowForm.Tests   --treenode-filter "/*/*/Measurements/*"
POW_MEASURE=1 dotnet test --project tests/PowForm.Screens --treenode-filter "/*/*/Measurements/*"
```

## What is dev-only

- Data Protection keys on the local disk, and the in-memory used-challenge store and message list. The keys are stored
  unencrypted (the app logs "No XML encryptor configured"); production adds `ProtectKeysWith...` (a certificate, Azure
  Key Vault, DPAPI) next to the shared key store.
- Antiforgery is off (`[IgnoreAntiforgeryToken]` on the page models, `asp-antiforgery="false"` on the forms). Razor Pages
  validates it by default; here there is no session or cookie to forge a request against, and a post without the token would
  be refused with a 400 for a reason that has nothing to do with the proof of work.
- Messages are only kept in memory and logged; nothing is mailed or stored.
- The proof of work is the only defence here. Real sites layer others, because a proof of work slows
  bots that run JavaScript but does not detect them.

## Licence

MIT No Attribution, see `LICENSE`.
