# cordite-site-temp

Starter static marketing site for Cordite Wars with:
- hero/section background image slots
- video and screenshot placeholder slots
- Stripe Checkout paywall hooks for downloads

## Stripe paywall wiring required

This folder includes front-end paywall hooks only. Implement these backend endpoints:

1. `POST /api/create-checkout-session`
   - Input: `{ "product": "windows" | "macos" | "linux" }`
   - Create a Stripe Checkout Session server-side with your secret key.
   - Return: `{ "id": "<checkout_session_id>" }`

2. `GET /api/download-entitlements?session_id=...`
   - Validate the Checkout Session with Stripe.
   - Return:
     ```json
     {
       "paid": true,
       "downloadUrls": {
         "windows": "https://.../CorditeWars_Setup.exe",
         "macos": "https://.../CorditeWars.dmg",
         "linux": "https://.../CorditeWars.snap"
       }
     }
     ```

## Setup notes

- Update `STRIPE_PUBLISHABLE_KEY` in `app.js`.
- Keep Stripe secret key only on the backend.
- Recommended: generate short-lived signed download URLs after payment verification.
