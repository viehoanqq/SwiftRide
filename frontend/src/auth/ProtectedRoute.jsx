import { Navigate } from 'react-router-dom';
import { useAuth } from './AuthContext';

export const homeOf = role => (role === 'Driver' ? '/driver' : '/rider');

// Chưa đăng nhập → /login. Sai vai trò → về trang của mình.
export default function ProtectedRoute({ role, children }) {
  const { user } = useAuth();
  if (!user) return <Navigate to="/login" replace />;
  if (role && user.role !== role) return <Navigate to={homeOf(user.role)} replace />;
  return children;
}
