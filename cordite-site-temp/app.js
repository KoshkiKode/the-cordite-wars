// Replace with your Stripe publishable key.
const STRIPE_PUBLISHABLE_KEY = "pk_test_replace_me";
const stripe = Stripe(STRIPE_PUBLISHABLE_KEY);

const paywallMessage = document.getElementById("paywall-message");
const buttons = document.querySelectorAll("button[data-product]");
const downloadLinks = document.querySelectorAll("a[data-download]");

for (const button of buttons) {
  button.addEventListener("click", async () => {
    const product = button.dataset.product;
    await startCheckout(product);
  });
}

async function startCheckout(product) {
  // Backend endpoint expected:
  // POST /api/create-checkout-session with JSON { product }
  // Response: { id: "cs_test_..." }
  const response = await fetch("/api/create-checkout-session", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ product })
  });

  if (!response.ok) {
    paywallMessage.textContent = `Unable to start payment (HTTP ${response.status}). Please try again or contact support.`;
    return;
  }

  const session = await response.json();
  await stripe.redirectToCheckout({ sessionId: session.id });
}

async function tryUnlockFromSession() {
  const params = new URLSearchParams(window.location.search);
  const sessionId = params.get("session_id");
  if (!sessionId) return;
  if (!/^cs_(test_|live_)?[A-Za-z0-9_]+$/.test(sessionId)) return;

  // Backend endpoint expected:
  // GET /api/download-entitlements?session_id=...
  // Response: { paid: true, downloadUrls: { windows, macos, linux } }
  const response = await fetch(`/api/download-entitlements?session_id=${encodeURIComponent(sessionId)}`);
  if (!response.ok) return;

  const result = await response.json();
  if (!result.paid) return;

  for (const link of downloadLinks) {
    const key = link.dataset.download;
    const href = result.downloadUrls?.[key];
    if (href) {
      link.href = href;
      link.classList.add("unlocked");
      link.removeAttribute("aria-disabled");
    }
  }
  paywallMessage.textContent = "✅ Payment verified. Downloads unlocked for this session.";
}

tryUnlockFromSession();
