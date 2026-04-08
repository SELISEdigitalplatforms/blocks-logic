# SeliseBlocks.IamDriver

## Overview

`SeliseBlocks.IamDriver` is an Identity and Access Management (IAM) driver that simplifies user creation and authentication management. It provides a standardized approach to handling user identities securely within your application.

## Installation

To install `SeliseBlocks.IamDriver`, add the NuGet package to your project:

```sh
dotnet add package SeliseBlocks.IamDriver
```

## Usage

### Register Dependencies

Before using `SeliseBlocks.IamDriver`, ensure that all required dependencies are registered in your application's dependency injection container. Add the following namespace in your `Program.cs`:

```csharp
using Blocks.Extension.DependencyInjection;
```

Register the IAM service:

```csharp
builder.Services.RegisterBlocksIamService();
builder.services.AddSingleton<IIamDriverService, IamDriverService>();
```

This method will configure and register all necessary services required for the IAM driver to function properly.

## Features

### Create User

To create a new user, instantiate the appropriate request object and invoke the `CreateUser` method.

```csharp
var request = new CreateUserRequest
{
    Email = "user@example.com",
    FirstName = "John",
    LastName = "Doe",
    Password = "securepassword",
    UserName = "johndoe",
    PhoneNumber = "+1234567890"
};

var response = await iamDriverService.CreateUser(request);
```

## Summary

`SeliseBlocks.IamDriver` provides a powerful and flexible way to manage user identities within your application. It ensures secure authentication and seamless user management while integrating smoothly with your existing system.

