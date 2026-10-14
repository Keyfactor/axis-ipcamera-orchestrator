## Overview

The AXIS IP Camera Orchestrator extension remotely manages certificates on AXIS IP Network Cameras. This
orchestrator extension inventories certificates on the camera's certificate store, and it also supports adding new identity certificates and adding/removing CA certificates.
New identity certificates are created in the AXIS camera certificate store via On Device Key Generation (ODKG), also known as Reenrollment.
This means that certificates cannot be directly added to the AXIS camera, but instead the keypair is generated on the AXIS device and a certificate is issued for that keypair via a CSR submitted to Command for enrollment. 
This workflow is completely automated in the AXIS IP Camera Orchestrator extension. CA certificates can be added to the camera from uploaded CA certificates in Command.

### Use Cases

#### Supported

1. Inventory of identity & CA certificates 
2. Enrollment of identity certificates with ability to bind the certificate for a specific usage*
3. Ability to remove CA certificates from the camera
4. Ability to add CA certificates to the camera

#### Not Supported

1. Ability to remove identity certificates from the camera
2. Ability to add identity certificates to the camera

\* Currently supported certificate usages include: **HTTPS**, **IEEE802.X**, **MQTT**, **Other**

## Requirements

1. Out of the box, an AXIS IP Network Camera will typically have configured an **Administrator** account. It is 
recommended to create a new account specifically for executing API calls. This account will need \'Administrator\' 
privileges since the orchestrator extension is capable of making configuration changes, such as installing and removing certificates.

### Camera Compatibility

Supported on AXIS cameras running AXIS OS 11.6 or later. Available LTS tracks vary by camera model.

- **Tested model:** AXIS M2035-LE Bullet Camera
- **Tested AXIS OS version:** 12.2.62

Has not been tested with any other model or firmware version.

### Authentication

The Axis IP Camera Orchestrator Extension uses .NET HttpClientHandler credential negotiation when connecting to Axis devices over HTTPS.
This allows the orchestrator to automatically negotiate the authentication mechanism required by the camera.
The orchestrator has been validated against Axis cameras configured with:

- Basic
- Digest
- Basic & Digest
- Recommended

As a result, customer-side changes to camera authentication policies are generally not required.

## Device Onboarding

Cameras are typically provisioned with a self-signed or otherwise untrusted device identity certificate. The
**Use SSL** and **Bypass TLS Validation** certificate store properties together determine what's required to
connect successfully:

| Use SSL | Bypass TLS Validation | Behavior |
| --- | --- | --- |
| `False` | *(any)* | Connects over plain HTTP. There is no TLS handshake, so certificate trust does not apply, and **Bypass TLS Validation** has no effect either way. |
| `True` | `True` | Connects over HTTPS but skips certificate validation entirely - any certificate is accepted, including the camera's untrusted factory certificate. |
| `True` | `False` | Connects over HTTPS and validates the certificate like any other TLS connection - see below for what's required to pass. |

> [!WARNING]
> It is highly recommended to keep **Use SSL** enabled. Plain HTTP sends credentials and certificate data to
> the camera unencrypted, and is only intended as a fallback for cameras or networks that cannot support HTTPS.

> [!TIP]
> **AXIS Device ID CA certificates:** https://www.axis.com/support/public-key-infrastructure-repository

All network cameras come pre-loaded with one (1) or more device ID certificates - potentially using different
key algorithms (e.g. RSA, ECC) - and one of these is configured on the camera to be presented during the TLS
handshake to the client during an HTTPS request.

When **Use SSL** is `True` and **Bypass TLS Validation** is `False`, install the issuing intermediate and root
CAs (linked above) for whichever device ID certificate is currently bound to the HTTP server into the
orchestrator server's local trust store so that standard TLS validation succeeds. For instructions on
replacing the camera's factory certificate with one enrolled from the customer's own PKI, see
[Replacing the Factory Device ID Certificate](#replacing-the-factory-device-id-certificate) below.

### Camera-Specific Trust Validation

This only applies when **Use SSL** is `True` and **Bypass TLS Validation** is `False`.

The camera's factory certificate's CN/SAN reflects whatever hostname or IP it was given at manufacture, not
the camera's current network address - so connecting to the camera at its real, current address normally
fails with a standard TLS name mismatch, even once the issuing CAs above are trusted. To resolve this for the
factory certificate specifically:

- Enter the camera's serial number (shown in the camera's web UI) as the certificate store's **Store Path**
  value.
- If a name mismatch is the *only* problem (the certificate otherwise chains to a trusted CA), the orchestrator
  checks the certificate's `SERIALNUMBER` Subject DN attribute - a separate field that identifies the physical
  device regardless of network address - against that Store Path value. If they match, the mismatch is waived.
- This doesn't change anything about trust itself: a certificate that doesn't chain to a trusted CA still
  fails, regardless of Store Path.

> [!NOTE]
> This check only compares the certificate's `SERIALNUMBER` against the recorded Store Path value - it does
> not verify which CA issued the certificate. It is not intended to distinguish the camera's factory
> certificate from a customer-issued certificate that happens to carry the same value, for example due to a
> misconfigured enrollment.

> [!NOTE]
> This only applies while the camera presents its factory certificate. Once a customer-PKI certificate is
> enrolled via ODKG, standard TLS validation against the store's actual Client Machine value takes over, as
> described above.

> [!IMPORTANT]
> Inventory, Reenrollment (ODKG), and Management jobs all connect to the camera using the same HTTP/HTTPS
> connection, so **Bypass TLS Validation** affects all of them.

## Enrollment Behavior

The following enrollment behaviors are specific to ODKG (On Device Key Generation) — also known as Reenrollment — on AXIS cameras, and should be considered when designing certificate automation workflows.

### Replacing the Factory Device ID Certificate

To move the camera off its factory certificate, schedule an ODKG job and select **HTTPS** as the Certificate Usage. This enrolls a new certificate issued from the customer's own PKI and binds it to the camera's HTTP server.

> [!IMPORTANT]
> After the ODKG job completes, make sure the orchestrator server trusts the newly enrolled certificate by installing its issuing intermediate and root CAs into the orchestrator server's local trust store. These are typically different CAs than the ones used for the camera's factory certificate.

### Alias Versioning

AXIS cameras require each Alias to be unique, and each Alias is tightly coupled with the private key used to generate its certificate. Because of this, certificates cannot be reenrolled in place by replacing the certificate and private key associated with an existing Alias.

To support certificate renewals and automation workflows, the orchestrator generates a unique Alias by appending the following suffix:

`_yyMMddHHmm`

where `yyMMddHHmm` represents the current UTC date and time.

From an automation perspective, the same Alias can continue to be reused, as uniqueness is enforced by the integration.

> [!NOTE]
> As of v1.1.0, ODKG jobs automatically manage versioned Aliases. When reenrolling a certificate using the same Alias, the integration creates a new certificate using a versioned Alias and removes the previously enrolled certificate associated with the same base Alias.
>
> Because AXIS cameras do not support in-place certificate replacement, a new versioned Alias is still created during reenrollment. The integration identifies and removes the previous certificate by matching the base Alias name and ignoring the timestamp suffix.
>
> If a new Alias is supplied during reenrollment, the original certificate (if one exists) associated with the selected `Certificate Usage` is **not** automatically removed from the camera. Because AXIS cameras have limited certificate and key storage capacity, users should periodically review and remove unused certificates through the AXIS Network Camera GUI.

#### Configuration Example

The following example demonstrates how Alias versioning behaves in an ODKG job configuration:

- **Store Path:** camera serial number, e.g. `0b7c3d2f9e8a`
- **Overwrite:** `true` or `false` *(has no bearing on Alias or certificate behavior)*
- **Alias:** `https-cert` *(the Alias that will appear on the camera)*
- **Certificate Usage:** any of `HTTPS`, `IEEE802.X`, `MQTT`, `Other` *(`Trust` is not supported for ODKG jobs — see [Certificate Usage Considerations (Trust)](#certificate-usage-considerations-trust) below)*

In this configuration:
- The ODKG job generates a new identity certificate and assigns it a versioned Alias by appending a `_yyMMddHHmm` timestamp suffix, e.g. `https-cert_2508251200`
- A later ODKG job using the same `https-cert` Alias and the same Certificate Usage creates another versioned Alias (e.g. `https-cert_2509031400`) and removes the certificate tied to the previous versioned Alias, matching on the base Alias name and ignoring the timestamp suffix
- If a different Alias is supplied on a later job, the original certificate associated with the selected Certificate Usage is **not** automatically removed and must be cleaned up manually via the AXIS Network Camera GUI

Operational behavior:
- The **Overwrite** setting has no effect on Alias or certificate behavior — AXIS cameras do not support in-place certificate replacement, so a new versioned Alias is always created regardless of how Overwrite is set
- The base Alias name (excluding the timestamp suffix) is what the integration uses to identify and remove a previously enrolled certificate

### Certificate Usage Considerations (Trust)

> [!NOTE]
> If an ODKG job is configured with `Trust` selected as the `Certificate Usage`, the job will return a warning indicating that the operation is not supported.
>
> Trust CA certificates must be installed using a **Management - Add** job. These certificates establish trust for TLS connections initiated by the camera.

### Subject Alternative Names (SANs)

As of Keyfactor Command v25.4, Subject Alternative Names (SANs) can be specified for ODKG jobs. Support for passing SANs to the orchestrator also requires, at minimum, Keyfactor Universal Orchestrator v25.1.

The AXIS IP Camera API only supports DNS and IP SAN types. Any other SAN types included in the ODKG job will be ignored and will not be added to the enrolled certificate.

> [!NOTE]
> If SANs are not provided and the selected `Certificate Usage` is `HTTPS`, IP and DNS SANs are automatically added when enrolling a certificate associated with a new Alias:
>
> - **IP** = The Client Machine configured for the certificate store (excluding any port number)
> - **DNS** = The Common Name (CN) specified in the certificate Subject DN

## Troubleshooting

### 401 Unauthorized Responses

The Axis IP Camera Orchestrator Extension supports authentication negotiation and has been validated against Axis cameras configured for:

- Basic
- Digest
- Basic & Digest
- Recommended

If a 401 Unauthorized response is encountered:

1. Verify the configured credentials.
2. Verify connectivity to the device.
3. Review orchestrator logs for authentication-related messages.

## Operational Notes

### AXIS OS 12 Firmware Recommendation

> [!IMPORTANT]
> Devices running the AXIS OS 12 release track should always be updated to the latest firmware version available from Axis. Previous firmware versions are no longer supported once a newer release becomes available.

Axis identified a memory leak in older firmware releases that may cause device keystore storage to become exhausted over time. If keystore storage issues are observed, verify that the device is running the latest supported firmware version.

## Release Notes

**1.2.0**
- Standardized device onboarding and TLS trust validation: `Use SSL` now actually controls whether the camera connection uses HTTP or HTTPS, a new `Bypass TLS Validation` certificate store property can skip certificate validation entirely, and the camera's factory certificate is now validated against the orchestrator server's standard local trust store instead of a bundled custom trust.
- Fixed Inventory reporting Success with zero certificates when the camera returned none.

**1.1.0**
- Improved exception handling to be more robust for HTTP client initialization.
- Implemented .NET native credential negotiation to support both Basic and Digest authentication policies.
- Fixed incorrect mapping of the ECDSA algorithm to ECP; updated to ECDSA.
- Implemented Alias versioning to enable reuse of existing Alias names in automation processes.
- Updated doctool release actions to v5.
- Updated .NET target frameworks to net8.0 and net10.0.

**1.0.2**
- Removed logging of plaintext cert store Server Password.
- Updated Keystore type to be dynamic instead of a fixed Enum, to allow compatibility across different cameras and firmware versions.

**1.0.1**
- Added screenshots to docs.

**1.0.0**
- Initial Public Version.
