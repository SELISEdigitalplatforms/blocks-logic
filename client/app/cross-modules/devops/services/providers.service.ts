const GITHUB_CLIENT_ID = import.meta.env.BLOCKS_GITHUB_CLIENT_ID || "";
const GITHUB_REDIRECT_URI = import.meta.env.BLOCKS_GITHUB_REDIRECT_URI || window.location.origin;

export const authenticateWithGithub = (extraState?: string) => {
  const state = extraState || Math.random().toString(36).substring(7);
  const scope = "repo,user";
  
  const authUrl = `https://github.com/login/oauth/authorize?client_id=${GITHUB_CLIENT_ID}&redirect_uri=${encodeURIComponent(
    GITHUB_REDIRECT_URI
  )}&scope=${scope}&state=${state}`;

  // Store current destination before redirect
  const destination = localStorage.getItem("destination") || "/";
  localStorage.setItem("github_auth_destination", destination);
  localStorage.setItem("github_auth_state", state);
  
  // Open GitHub OAuth in same window
  window.location.href = authUrl;
};

export const authenticateWithGitlab = () => {
  console.log("GitLab authentication not yet implemented");
  // Placeholder for GitLab OAuth
};

export const authenticateWithBitbucket = () => {
  console.log("Bitbucket authentication not yet implemented");
  // Placeholder for Bitbucket OAuth
};

export const authenticateWithAzure = () => {
  console.log("Azure DevOps authentication not yet implemented");
  // Placeholder for Azure OAuth
};

export const authenticateWithAws = () => {
  console.log("AWS CodeCommit authentication not yet implemented");
  // Placeholder for AWS authentication
};
