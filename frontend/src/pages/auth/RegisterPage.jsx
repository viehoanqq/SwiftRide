import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { authApi } from '../../api/authApi';
import { getErrorMessage } from '../../api/client';

export default function RegisterPage() {
  const navigate = useNavigate();
  const [form, setForm] = useState({ fullName: '', phone: '', password: '', confirm: '', role: 'Rider' });
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const update = field => e => setForm({ ...form, [field]: e.target.value });

  const handleSubmit = async e => {
    e.preventDefault();
    setError('');
    if (form.password !== form.confirm) {
      setError('Mật khẩu nhập lại không khớp');
      return;
    }
    setLoading(true);
    try {
      const { fullName, phone, password, role } = form;   // không gửi "confirm" lên server
      await authApi.register({ fullName, phone, password, role });
      alert('Đăng ký thành công, mời bạn đăng nhập');
      navigate('/login');
    } catch (err) {
      setError(getErrorMessage(err));                     // hiện message 400/409 từ backend
    } finally {
      setLoading(false);
    }
  };

  return (
    <div>
      <h1>Đăng ký</h1>
      <form onSubmit={handleSubmit}>
        <div><label>Họ tên <input value={form.fullName} onChange={update('fullName')} required /></label></div>
        <div><label>Số điện thoại <input value={form.phone} onChange={update('phone')} required /></label></div>
        <div><label>Mật khẩu <input type="password" value={form.password} onChange={update('password')} required /></label></div>
        <div><label>Nhập lại mật khẩu <input type="password" value={form.confirm} onChange={update('confirm')} required /></label></div>
        <div>
          <label><input type="radio" value="Rider" checked={form.role === 'Rider'} onChange={update('role')} /> Người đi xe</label>
          <label><input type="radio" value="Driver" checked={form.role === 'Driver'} onChange={update('role')} /> Tài xế</label>
        </div>
        {error && <p>{error}</p>}
        <button type="submit" disabled={loading}>Đăng ký</button>
      </form>
      <p>Đã có tài khoản? <Link to="/login">Đăng nhập</Link></p>
    </div>
  );
}
