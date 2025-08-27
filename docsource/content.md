## Overview

The AXIS IP Camera Orchestrator extension remotely manages certificates on AXIS IP Network Cameras. This
orchestrator extension inventories certificates on the camera's certificate store, and it also supports adding new client-server certificates and adding/removing CA certificates.
New client-server certificates are created in the AXIS camera certificate store via On Device Key Generation (ODKG aka Reenrollment).
This means that certificates cannot be directly added to the AXIS camera, but instead the keypair is generated on the AXIS device and a certificate is issued for that keypair via a CSR submitted to Command for enrollment. 
This workflow is completely automated in the AXIS IP Camera Orchestrator extension. CA certificates can be added to the camera from uploaded CA certificates in Command.

### Use Cases

The AXIS IP Camera Orchestrator extension supports the following use cases:

1. Inventory of client-server & CA certificates 
2. Enrollment of client-server certificates with ability to bind the certificate for a specific usage*
3. Ability to remove CA certificates from the camera
4. Ability to add CA certificates to the camera

The Axis IP Camera Orchestrator extension DOES NOT support the following use cases:

1. Ability to remove client-server certificates from the camera
2. Ability to add client-server certificates to the camera

\* Currently supported certificate usages include: **HTTPS**, **IEEE802.X**, **MQTT**, **Other**

## Requirements

1. Out of the box, an AXIS IP Network Camera will typically have configured an **Administrator** account. It is 
recommended to create a new account specifically for executing API calls. This account will need \'Administrator\' 
privileges since the orchestrator extension is capable of making configuration changes, such as installing and removing certificates.
2. Currently supports AXIS M2035-LE Bullet Camera, AXIS OS version 12.2.62. Has not been tested with any other firmware version.

## Post Installation

The AXIS IP Camera Orchestrator Extension *always* connects to an AXIS IP Network Camera via HTTPS, regardless
of whether the \`Use SSL\` option on the certificate store is set to **false** (*The \`Use SSL\` option cannot be removed). This ensures the orchestrator
is connecting to a valid camera.

All network cameras come pre-loaded with one (1) or more device ID certificates, and one of these certificates is configured on the camera to be provided in the TLS handshake
to the client during an HTTPS request.

The orchestrator will not trust the device ID certificate, and will therefore deny the session to the camera.

To trust the device ID certificate, you must create a custom trust and add the root and intermediate CA certificates from the AXIS PKI chain to it.

### Steps to Create the Custom Trust:

1. Once the DLLs from GitHub are installed, create two (2) files in `..\[AXIS IP Camera orchestrator extension folder name]\Files` folder with the below names:
   * **Axis.Trust**
   * **Axis.Intermediate**

2. Copy and paste the PEM contents of the AXIS PKI root for the device ID cert configured for the HTTP server into the **Axis.Root** file
3. Copy and paste the PEM contents of the AXIS PKI intermediate for the device ID configured for the HTTP server into the **Axis.Intermediate** file

\* AXIS Device ID CA certificates can be found here: https://www.axis.com/support/public-key-infrastructure-repository

After the device ID is verified against the custom trust, the \`Store Path\` value of the certificate store will be compared against the SERIALNUMBER Subject DN attribute of the device ID certificate.
These values must match or the session will be denied.

> [!IMPORTANT]
> You will want to replace the device ID certificate bound to the HTTP server with a CA-signed certificate. To do this,
> you will need to schedule a Reenrollment job and select **HTTPS** as the Certificate Usage.

> [!IMPORTANT]
> After associating a CA-signed certificate with the HTTP server via the Reenrollment job, you need to make sure the orchestrator server trusts the HTTPS certificate.
> Therefore, you will need to install the full CA chain - including root and intermediate certificates - into the orchestrator server's local
> certificate store.

## Caveats

> [!NOTE] Reenrollment jobs will not replace or remove a client-server certificate with the same alias. They will also not remove 
> the original certificate if a particular \`Certificate Usage\` had an associated cert. Since the camera has limited storage,
> it will be up to the user to remove any unused client-server certificates via the AXIS Network Camera GUI.
