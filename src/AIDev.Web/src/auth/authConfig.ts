// MSAL configuration for Microsoft Entra ID authentication
// Update these values after creating your App Registration in Azure Portal

export const msalConfig = {
  auth: {
    clientId: "16417242-11f1-4548-add4-c631568df68a",
    authority: "https://login.microsoftonline.com/cb7dd1e7-8cf6-40c0-b19c-c8a0ff2adf93",
    redirectUri: "http://localhost:5173", // Must match App Registration redirect URI
  },
  cache: {
    cacheLocation: "sessionStorage" as const,
    storeAuthStateInCookie: false,
  },
};

// Scopes for the API
export const apiScopes = {
  scopes: ["api://1d4f6501-5d39-470a-8b57-57c9fd328836/access_as_user"],
};

// Scopes for login
export const loginRequest = {
  scopes: ["User.Read", "openid", "profile", "email"],
};
