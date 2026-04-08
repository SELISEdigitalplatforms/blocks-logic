# SeliseBlocks.MfaDriver

## Overview

`SeliseBlocks.MfaDriver` is a MFA driver designed to integrate MFA functionalities into your application. It provides a standardized way to generate, verify and manage MFA.

## Installation

To install `SeliseBlocks.MfaDriver`, add the NuGet package to your project:

```sh
dotnet add package SeliseBlocks.MfaDriver
```

## Usage

### Register Dependencies

Before using `SeliseBlocks.MfaDriver`, ensure that all required dependencies are registered in your application's dependency injection container. Add the following line in your `Program.cs`:

use the namespace

```csharp
using Blocks.Extension.DependencyInjection;
```

register the service

```csharp
builder.Services.RegisterBlocksMfaService();
```

This method will configure and register all necessary services required for the MFA driver to function properly.

## Features

- Generate OTP

  ```csharp
  var request = new OtpGenerationRequest
  {
      UserId = <string>,
      ProjectKey = <string>
  }
  ```

  Invoke `GenerateOtpAsync`

  ```csharp
  await GenerateOtpAsync(request);
  ```

- Verify OTP

  ```csharp
  var request = new VerifyOtpAsync
  {
      VerificationCode = <string>,
      TwoFactorId = <string>,
      AuthType = <Enum>, //None,TOTP,Email
      ProjectKey = <string>
  }
  ```

  Invoke `VerifyOtpAsync`

  ```csharp
  await VerifyOtpAsync(request);
  ```

- Manage User MFA

  ```csharp
  var userMfa = new UserMfaConfiguration
  {
      UserId = <string>,
      MfaEnabled = <string>,
      AuthType = <Enum>, //None,TOTP,Email
      ProjectKey = <string>
  }
  ```

  Invoke `ManageUserMfaAsync`

  ```csharp
  await ManageUserMfaAsync(request);
  ```

use the below namespace before invoking any of the methods.

```csharp
using Blocks.MfaDriver;
```
