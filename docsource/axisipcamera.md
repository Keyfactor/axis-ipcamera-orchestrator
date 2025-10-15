## Overview

The AXIS IP Camera certificate store type represents a certificate store on an AXIS network camera
that maintains two separate collections of certificates:
* Client-server certificates (certs with private keys)
* CA certificates

It is expected that there be one (1) certificate store managed per AXIS network camera.

## Requirements

1. User Account with \'Administrator\' privileges and password to access the camera
2. Camera serial number
3. Camera IP address (and likely port number)

## Certificate Usage

Every certificate inventoried will have an Entry Parameter called \`Certificate Usage\`. 
There are five (5) possible options:

* **HTTPS**
* **IEEE802.X**
* **MQTT**
* **Trust**
* **Other**

1. HTTPS
   - This certificate usage describes the certificate bound to the camera's HTTP web server for HTTPS communication (i.e. server certificate or SSL/TLS certificate).
2. IEEE802.X
   - This certificate usage describes the client certificate to authenticate the camera to a server using EAP-TLS. This client certificate
   is presented to the 802.1x radius server for authentication.
3. MQTT
   - This certificate usage describes the client certificate used to authenticate the camera to the MQTT broker.
   In this scenario, the camera connects to the MQTT broker over SSL and performs a TLS handshake.
   The camera presents this client certificate to the MQTT broker.
4. Trust
   - This certificate usage describes a public certificate issued by a CA used to establish trust. 
5. Other
   - This certificate usage identifies all other certificates on the camera that do not fall under the pre-defined usages above.

> [!NOTE] 
> A Reenrollment (ODKG) job will not allow enrollment of certificates with **Trust** assigned as the \`Certificate Usage\`.
> Trust CA certificates can be added to the camera via a Management - Add job.

> [!NOTE]
> For a Reenrollment (ODKG) job, where the \`Certificate Usage\` assigned is **HTTPS**, IP and DNS are added as SANS
> to the enrolled certificate.
> 
> IP = Client Machine configured for the certificate store (excluding any port)
> 
> DNS = CN set in the Subject DN