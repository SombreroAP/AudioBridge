# Licensing notes

## VB-CABLE (VB-Audio Software)

AudioBridge depends on VB-CABLE for the microphone direction. Per
[VB-Audio's licensing terms](https://vb-audio.com/Services/licensing.htm), an application
may bundle or depend on the base VB-CABLE provided the end user:

1. can see and identify VB-CABLE as a VB-Audio application;
2. is told its origin, `www.vb-cable.com`;
3. is told it is donationware, and is able to donate.

Consequences we design around:

- **Do not bundle the installer.** Detect VB-CABLE; if absent, show a setup step that
  credits VB-Audio, states the donationware model, and links to vb-cable.com. This
  satisfies all three conditions without ambiguity.
- **Only the base CABLE is ever an option.** VB-CABLE A+B and C+D are explicitly
  forbidden from being distributed or bundled with another product. We therefore design
  for exactly **one** virtual device. If a future feature needs more independent streams,
  the user must buy and install A+B themselves.
- **Professional use is not free.** The donationware model covers consumers. Distribution
  into businesses, where employees cannot personally donate, falls under volume licensing
  (roughly EUR 3.61-5.00 per unit).
- **Over 10 units of distribution requires a written agreement** with VB-Audio. If
  AudioBridge is ever sold or distributed at any scale, contact them first.

VB-Audio suggests bundling companies donate meaningfully; they cite USD 500-2,000.

*Not legal advice. Get VB-Audio's written agreement before any commercial distribution.*
