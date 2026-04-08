# SeliseBlocks.CaptchaDriver

## Overview

`SeliseBlocks.CaptchaDriver` is a Captcha driver designed to integrate captcha functionalities into your application. It provides a standardized way to create, submit and verify captcha.

## Installation

To install `SeliseBlocks.CaptchaDriver`, add the NuGet package to your project:

```sh
dotnet add package SeliseBlocks.CaptchaDriver
```

## Usage

### Register Dependencies

Before using `SeliseBlocks.CaptchaDriver`, ensure that all required dependencies are registered in your application's dependency injection container. Add the following line in your `Program.cs`:

use the namespace

```csharp
using Blocks.Extension.DependencyInjection;
```

register the service

```csharp
builder.Services.RegisterBlocksCaptchaService();
```

This method will configure and register all necessary services required for the Captcha driver to function properly.

## Features

- Create Captcha

  ```csharp
  var request = new CreateCaptchaRequest
  {
      ConfigurationName = <string>
  }
  ```

  Invoke `Create`

  ```csharp
  await Create(request);
  ```

- Submit Captcha

  ```csharp
  var request = new SubmitCaptchaRequest
  {
      Id = <string>,
      Value = <string>
  }
  ```

  Invoke `Submit`

  ```csharp
  await Submit(request);
  ```

- Verify Captcha

  ```csharp
  var request = new VerifyCaptchaRequest
  {
      VerificationCode = <string>,
      ConfigurationName = <string>
  }
  ```

  Invoke `Verify`

  ```csharp
  await Verify(request);
  ```

use the below namespace before invoking any of the methods.

```csharp
using Blocks.CaptchaDriver;
```
