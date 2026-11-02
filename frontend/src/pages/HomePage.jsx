import { useEffect, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { authApi } from '../api/authApi';
import { getErrorMessage } from '../api/client';

// Trang chủ tạm cho cả Rider và Driver: gọi GET /auth/me (cần token) để kiểm tra header đã gửi đúng
export default function HomePage() {
  const { user, logout } = useAuth();
  const [me, setMe] = useState(null);
  const [error, setError] = useState('');

  useEffect(() => {
    authApi.me().then(setMe).catch(err => setError(getErrorMessage(err)));
  }, []);

  return (
    <div>
      <h1>Xin chào, {user.name}</h1>
      <p>Vai trò: {user.role === 'Driver' ? 'Tài xế' : 'Người đi xe'}</p>
      {me && (
        <ul>
          <li>Mã: {me.id}</li>
          <li>Số điện thoại: {me.phone}</li>
        </ul>
      )}
      {error && <p>{error}</p>}
      <button onClick={logout}>Đăng xuất</button>
    </div>
  );
}
