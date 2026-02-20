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
      <div>
        <nav className="bg-white border-b border-slate-200 px-8 h-[60px] flex items-center justify-between shadow-sm sticky top-0 z-50">
          <div className="flex items-center gap-2">
            <span className="text-2xl">⚡</span>
            <span className="text-xl font-bold text-slate-800">AI Dev Pipeline</span>
          </div>
          <div className="flex items-center gap-1">
            {isAuthenticated && (
              <>
                <NavLink
                  to="/"
                  end
                  className={({ isActive }) =>
                    `no-underline px-4 py-2 rounded-lg font-medium transition-all duration-150 ${
                      isActive
                        ? "bg-primary text-white"
                        : "text-muted hover:bg-slate-100 hover:text-slate-800"
                    }`
                  }
                >
                  Requests
                </NavLink>
                <NavLink
                  to="/dashboard"
                  className={({ isActive }) =>
                    `no-underline px-4 py-2 rounded-lg font-medium transition-all duration-150 ${
                      isActive
                        ? "bg-primary text-white"
                        : "text-muted hover:bg-slate-100 hover:text-slate-800"
                    }`
                  }
                >
                  Dashboard
                </NavLink>
                <NavLink
                  to="/new"
                  className={({ isActive }) =>
                    `no-underline px-4 py-2 rounded-lg font-medium transition-all duration-150 ml-2 ${
                      isActive
                        ? "bg-primary text-white"
                        : "text-muted hover:bg-slate-100 hover:text-slate-800"
                    }`
                  }
                >
                  + New
                </NavLink>
              </>
            )}
            {isAuthenticated ? (
              <div className="flex items-center gap-3 ml-4 pl-4 border-l border-slate-200">
                <span className="text-sm text-muted font-medium">
                  {accounts[0]?.name || accounts[0]?.username}
                </span>
                <button
                  className="inline-flex items-center justify-center px-3 py-1 text-sm font-medium rounded-lg bg-slate-100 text-slate-800 border border-slate-200 hover:bg-slate-200 cursor-pointer transition-all duration-150"
                  onClick={handleLogout}
                >
                  Sign Out
                </button>
              </div>
            ) : (
              <button
                className="inline-flex items-center justify-center px-5 py-2 text-sm font-medium rounded-lg bg-primary text-white hover:bg-primary-hover cursor-pointer transition-all duration-150"
                onClick={handleLogin}
              >
                Sign In
              </button>
            )}
          </div>
        </nav>

        <main className="max-w-[1200px] mx-auto p-8">
          <AuthenticatedTemplate>
            <Routes>
              <Route path="/" element={<RequestList />} />
              <Route path="/new" element={<RequestForm />} />
              <Route path="/requests/:id" element={<RequestDetail />} />
              <Route path="/dashboard" element={<DashboardView />} />
            </Routes>
          </AuthenticatedTemplate>

          <UnauthenticatedTemplate>
            <div className="text-center py-24 px-8 max-w-[500px] mx-auto">
              <h1 className="text-3xl font-bold mb-4 text-slate-800">AI Dev Pipeline</h1>
              <p className="text-muted mb-8 text-lg leading-relaxed">
                Sign in with your Microsoft account to submit and track development requests.
              </p>
              <button
                className="inline-flex items-center justify-center px-8 py-3 text-base font-medium rounded-lg bg-primary text-white hover:bg-primary-hover cursor-pointer transition-all duration-150"
                onClick={handleLogin}
              >
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
