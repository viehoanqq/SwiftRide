import { useState } from 'react';
import { Link, Navigate, useNavigate } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext';
import { homeOf } from '../../auth/ProtectedRoute';
import { getErrorMessage } from '../../api/client';

export default function LoginPage() {
  const { user, login } = useAuth();
  const navigate = useNavigate();
  const [phone, setPhone] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  if (user) return <Navigate to={homeOf(user.role)} replace />;   // đã đăng nhập thì không ở lại trang login

  const handleSubmit = async e => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const current = await login(phone, password);
      navigate(homeOf(current.role));                               // Rider → /rider, Driver → /driver
    } catch (err) {
      setError(getErrorMessage(err));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div>
      <h1>Đăng nhập</h1>
      <form onSubmit={handleSubmit}>
        <div>
          <label>Số điện thoại <input value={phone} onChange={e => setPhone(e.target.value)} required /></label>
        </div>
        <div>
          <label>Mật khẩu <input type="password" value={password} onChange={e => setPassword(e.target.value)} required /></label>
        </div>
        {error && <p>{error}</p>}
        <button type="submit" disabled={loading}>Đăng nhập</button>
      </form>
      <p>Chưa có tài khoản? <Link to="/register">Đăng ký</Link></p>
    </div>
  );
}
