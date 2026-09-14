using System.Runtime.Versioning;

// fnctl inspects the Docker socket, /etc/blocks-runner and the runner's own Linux-only
// maintenance code. It is an operator tool for this VM, not a cross-platform client.
[assembly: SupportedOSPlatform("linux")]
