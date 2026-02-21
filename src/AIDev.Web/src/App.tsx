import { BrowserRouter, Routes, Route, NavLink } from "react-router-dom";
import {
  useIsAuthenticated,
  useMsal,
  AuthenticatedTemplate,
  UnauthenticatedTemplate,
} from "@azure/msal-react";
import { loginRequest } from "./auth/authConfig";
import RequestList from "./components/RequestList";
import RequestForm from "./components/RequestForm";
import RequestDetail from "./components/RequestDetail";
import DashboardView from "./components/Dashboard";
import AdminSettings from "./components/AdminSettings";
import "./App.css";

function App() {
  const { instance, accounts } = useMsal();
  const isAuthenticated = useIsAuthenticated();

  const handleLogin = () => {
    instance.loginRedirect(loginRequest).catch(console.error);
  };

  const handleLogout = () => {
    instance.logoutRedirect().catch(console.error);
  };

  return (
    <BrowserRouter>
      <div className="app">
        <nav className="navbar">
          <div className="nav-brand">
            <span className="nav-logo">⚡</span>
            <span className="nav-title">Silver Bullet Labs</span>
          </div>
          <div className="nav-links">
            {isAuthenticated && (
              <>
                <NavLink to="/" end>
                  Requests
                </NavLink>
                <NavLink to="/dashboard">Dashboard</NavLink>
                <NavLink to="/admin">Admin</NavLink>
                <NavLink to="/new" className="nav-new-btn">
                  + New
                </NavLink>
              </>
            )}
            {isAuthenticated ? (
              <div className="nav-user">
                <span className="nav-user-name">
                  {accounts[0]?.name || accounts[0]?.username}
                </span>
                <button className="btn btn-secondary btn-sm" onClick={handleLogout}>
                  Sign Out
                </button>
              </div>
            ) : (
              <button className="btn btn-primary" onClick={handleLogin}>
                Sign In
              </button>
            )}
          </div>
        </nav>

        <main className="main-content">
          <AuthenticatedTemplate>
            <Routes>
              <Route path="/" element={<RequestList />} />
              <Route path="/new" element={<RequestForm />} />
              <Route path="/requests/:id" element={<RequestDetail />} />
              <Route path="/dashboard" element={<DashboardView />} />
              <Route path="/admin" element={<AdminSettings />} />
            </Routes>
          </AuthenticatedTemplate>

          <UnauthenticatedTemplate>
            <div className="login-prompt">
              <h1>Silver Bullet Labs</h1>
              <p>Sign in with your Microsoft account to submit and track development requests.</p>
              <button className="btn btn-primary btn-lg" onClick={handleLogin}>
                Sign in with Microsoft
              </button>
            </div>
          </UnauthenticatedTemplate>
        </main>
      </div>
    </BrowserRouter>
  );
}

export default App;
