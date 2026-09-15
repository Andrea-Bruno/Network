# NetworkManager

NetworkManager is a small .NET library that lets your application take part in a
peer-to-peer network. Your program can join an existing network as a node (and help
keep it decentralised) or simply connect to it as an external user. It takes care
of sending objects between machines over HTTP, of signing them, and of sharing data
through a common pipeline that keeps everything in chronological order.

A handy extra: the library can simulate a whole peer-to-peer network inside a
single machine using "virtual devices". This means you can create several nodes and
watch them talk to each other without any internet connection, which is very useful
for trying the library out or for testing your own code.

## Requirements

The library targets `netstandard2.0`, so it runs on a wide range of platforms,
including .NET Framework 4.6.1 or newer, .NET Core 2.0 or newer, .NET 5 or newer,
Mono and Unity.

To build it from source you only need a recent .NET SDK (6.0 or newer is fine).
An IDE such as Visual Studio, JetBrains Rider or Visual Studio Code is optional.

You can check whether you already have the SDK installed by running:

```
dotnet --version
```

If you do not have it yet, you can download it from
https://dotnet.microsoft.com/download.

## Building the library

From the root of the repository run:

```
dotnet build Network/NetworkManager.csproj -c Release
```

When the build finishes you will find the compiled library at
`Network/bin/Release/netstandard2.0/NetworkManager.dll`.

## Using it in your own project

Add a reference to the library from your own C# project:

```
dotnet add reference <path>/Network/NetworkManager.csproj
```

Then add `using NetworkManager;` at the top of your code.

## Trying it out locally (no internet needed)

The easiest way to see the library working is to create a small "virtual" network
on your own machine. A `VirtualDevice` stands in for a machine that is connected to
the network, and the nodes built on top of it exchange their messages through local
pipes instead of the internet.

The example below creates a node, registers a small action that reacts to a message
of type `HelloMessage`, and then sends such a message to the node through the
simulated network:

```csharp
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using NetworkManager;

class Program
{
    static void Main()
    {
        // A private key is used to identify the node and to sign what it sends.
        var rsa = new RSACryptoServiceProvider(1024);
        var privateKey = Convert.ToBase64String(rsa.ExportCspBlob(true));

        // A virtual device plays the role of a machine connected to the network.
        var myDevice = new VirtualDevice();
        var myNode = new NodeInitializer(privateKey, myDevice);

        // A second virtual device used as the first contact point of the network.
        var peer = new VirtualDevice();
        var entryPoints = new List<Node>
        {
            new Node { Address = peer.Address, MachineName = peer.MachineName, PublicKey = "" }
        };

        // Join a network called "testnet" as a node.
        var network = new NetworkConnection(entryPoints, "testnet", myNode);

        // Decide what to do when a HelloMessage arrives.
        network.Protocol.AddOnReceivingObjectAction("HelloMessage", xml =>
        {
            Console.WriteLine("Node received: " + xml);
            return "ok";
        });

        // Send a message to the node through the simulated network.
        network.Communication.SendObjectSync(
            new HelloMessage { Text = "ciao-mondo" },
            myDevice.Address,
            toUser: Environment.MachineName);
    }
}

public class HelloMessage
{
    public string Text;
}
```

Because everything happens locally, you can run this right away and see the message
arrive at the node. You can create as many virtual devices as you like to simulate a
bigger network. To "unplug" a virtual machine from its simulated internet, set its
`NetSpeed` to `0` (or `IsOnline` to `false`); set it back to a positive value to
bring it online again.

## Running a real node

To run a real node that other machines can reach over the internet, you host a
normal web endpoint and forward the incoming requests to the library. The library
exposes a single handler for this purpose:

```csharp
bool OnReceivesHttpRequest(NameValueCollection queryString,
                           NameValueCollection form,
                           string fromIp,
                           out string contentType,
                           Stream outputStream);
```

Wire your web server so that, for every incoming request, you call this handler with
the request's query string, form data and the caller's IP address, and you send
back whatever the handler writes to `outputStream` using the returned
`contentType`. The node then takes care of the rest of the protocol.

When you create the `NetworkConnection`, pass the address your machine can be
reached at from the outside (through the `NodeInitializer`) and the list of
entry-point nodes that let you join the network. If you leave the node information
out, your program connects as an external user instead of becoming a node.

## A few notes

- For experiments the network name `testnet` is recommended, as it keeps your tests
  separate from any real network.
- The library keeps track of whether the machine is online and retries automatically
  when the connection comes back.
- The included code is a work in progress. If you run into trouble, open an issue on
  the GitHub repository and describe what you were trying to do.
