import { Navigate } from 'react-router-dom';
import { useAuth } from './AuthContext';

export const homeOf = role => {
  if (role === 'Driver') return '/driver';
  if (role === 'Admin') return '/admin';
  return '/rider';
};

// Chưa đăng nhập → /login. Sai vai trò → về trang của mình.
export default function ProtectedRoute({ role, roles, children }) {
  const { user } = useAuth();
  if (!user) return <Navigate to="/login" replace />;
  if (role && user.role !== role) return <Navigate to={homeOf(user.role)} replace />;
  if (roles && !roles.includes(user.role)) return <Navigate to={homeOf(user.role)} replace />;
  return children;
}
