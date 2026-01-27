# App Security Checklist

1. Don't talk to the database directly - Use middleware/backend API.

2. Gatekeep every single action - Check permissions for each endpoint.

3. Don't hide, withhold - Verify premium access on server.

4. Keep secrets off the browser - Store API keys on server.

5. Using .env doesn't mean safe - Keys could leak to client.

6. Don't do math on the phone - Perform calculations on server.

7. Sanitize everything - Treat inputs as text, not commands.

8. Put a speed limit on buttons - Implement rate limiting.

9. Don't log sensitive stuff - Avoid logging passwords/tokens.

10. Audit with a rival - Use another AI for security audit.

11. Keep dependencies up to date - Update libraries to patch vulnerabilities.

12. Proper error handling - Keep messages vague, log privately.

Source: arxiv.org/html/2512.0326…


# Flutterwave Test Cards

## Successful Card (No PIN, No OTP)
| Field | Value |
|-------|-------|
| Card Number | `4242424242424242` |
| CVV | `812` |
| Expiry | `01/39` |
| PIN | Not required |
| OTP | Not required |

## Successful Card (With PIN + OTP)
| Field | Value |
|-------|-------|
| Card Number | `5531886652142950` |
| CVV | `564` |
| Expiry | `09/32` |
| PIN | `3310` |
| OTP | `12345` |

## Declined Card
| Field | Value |
|-------|-------|
| Card Number | `5258585922666506` |
| CVV | `883` |
| Expiry | `09/31` |
| PIN | `3310` |
| OTP | `12345` |

## Insufficient Funds Card
| Field | Value |
|-------|-------|
| Card Number | `5399838383838381` |
| CVV | `470` |
| Expiry | `10/31` |
| PIN | `3310` |
| OTP | `12345` |

---

### Quick Copy Reference