using Functions.DomainService.Entities;

namespace Functions.DomainService.Utils
{
    /// <summary>
    /// The image reference a run of a deployed version is dispatched with.
    /// <para>
    /// A version built the registry way carries a digest-pinned <see cref="FunctionVersionEntity.ImageDigest"/>
    /// and runs from that. A version built the artifact way (the build uploaded a tarball to the tenant's
    /// storage instead of pushing to a registry) has no digest at all — the runner reports none — so its
    /// <c>ImageDigest</c> is empty. The runner still needs a reference: it is the name under which it
    /// builds the image from the downloaded artifact, and with an artifact on the run entry it never
    /// pulls that name from a registry (<c>ImageResolver.EnsureAsync</c>). An empty one made the runner
    /// dead-letter every run of such a version ("the entry carries no image").
    /// </para>
    /// <para>
    /// The name is derived from the artifact id, so it is stable for the version, distinct per build,
    /// and lets a host that already built it reuse its local image.
    /// </para>
    /// </summary>
    public static class FunctionRunImage
    {
        /// <summary>The reference for runs of <paramref name="version"/>; empty only when it has neither.</summary>
        public static string For(FunctionVersionEntity version)
        {
            ArgumentNullException.ThrowIfNull(version);
            if (!string.IsNullOrWhiteSpace(version.ImageDigest)) return version.ImageDigest;
            if (!string.IsNullOrWhiteSpace(version.ArtifactId)) return ForArtifact(version.ArtifactId);
            return string.Empty;
        }

        /// <summary><c>blocks-fn-artifact/{artifactId}:local</c> — lower-case, as Docker requires.</summary>
        public static string ForArtifact(string artifactId) =>
            $"blocks-fn-artifact/{artifactId.Trim().ToLowerInvariant()}:local";
    }
}
