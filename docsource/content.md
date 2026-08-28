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

The AXIS IP Camera Orchestrator Extension *always* connects to an AXIS IP Network Camera via HTTPS, regardless of how the **Use SSL** option on the certificate store is configured. This ensures that the orchestrator always validates the camera's certificate against the configured trust before proceeding.

All network cameras come pre-loaded with one (1) or more device ID certificates, and one of these certificates is configured on the camera to be provided in the TLS handshake
to the client during an HTTPS request.

The orchestrator will not trust the device ID certificate, and will therefore deny the session to the camera.

To trust the device ID certificate, you must create a custom trust and add the root and intermediate CA certificates from the AXIS PKI chain to it.

### Steps to Create the Custom Trust

1. Once the DLLs from GitHub are installed, create two (2) files in the sub-directory called "Files" with the below names (*Note: The "Files" folder should already exist):
   * **Axis.Root**
   * **Axis.Intermediate**

* **Default Path on Windows** - `C:\Program Files\Keyfactor\Keyfactor Orchestrator\extensions\[Axis IP Camera orchestrator extension folder]\Files`
* **Default Path on Linux** - `/opt/keyfactor/orchestrator/extensions/[Axis IP Camera orchestrator extension folder]/Files`
2. Copy and paste the PEM contents of the AXIS PKI root for the device ID cert configured for the HTTP server into the **Axis.Root** file
3. Copy and paste the PEM contents of the AXIS PKI intermediate for the device ID configured for the HTTP server into the **Axis.Intermediate** file

\* AXIS Device ID CA certificates can be found here: https://www.axis.com/support/public-key-infrastructure-repository

> [!IMPORTANT]
> You will want to replace the device ID certificate bound to the HTTP server with a CA-signed certificate. To do this,
> you will need to schedule an ODKG job and select **HTTPS** as the Certificate Usage.

> [!IMPORTANT]
> After associating a CA-signed certificate with the HTTP server via the ODKG job, you need to make sure the orchestrator server trusts the HTTPS certificate.
> Therefore, you will need to install the full CA chain - including root and intermediate certificates - into the orchestrator server's local
> certificate store.

### Camera-Specific Trust Validation

After the device ID is verified against the custom trust, the **Store Path** value of the certificate store will be compared against the SERIALNUMBER Subject DN attribute of the device ID certificate.
These values must match or the session will be denied.

> [!NOTE]
> This SERIALNUMBER validation only applies while the camera's factory device ID certificate remains bound to the HTTP server. Once a new certificate is enrolled via an ODKG job using the customer's PKI, the camera presents that certificate instead, and the orchestrator falls back to standard TLS certificate chain validation — requiring the customer's root and intermediate CA certificates to be installed in the orchestrator server's local certificate store.

## Enrollment Behavior

The following enrollment behaviors are specific to ODKG (On Device Key Generation) — also known as Reenrollment — on AXIS cameras, and should be considered when designing certificate automation workflows.

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
