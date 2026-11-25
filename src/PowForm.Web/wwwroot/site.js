// Turns the proof of work on for every form that names a purpose: <form data-proof="contact">.
import { guard } from "./proof.js";

for (const form of document.querySelectorAll("form[data-proof]")) {
  guard(form, form.dataset.proof);
}
