import { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext';
import { authApi } from '../../api/authApi';
import { tripApi } from '../../api/tripApi';
import { getErrorMessage } from '../../api/client';
import { getRecentTrips, rememberTrip } from '../../utils/recentTrips';

export default function RiderHomePage() {
  const { user, logout } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const [me, setMe] = useState(null);
  const [currentTrip, setCurrentTrip] = useState(null);
  const [checking, setChecking] = useState(true);
  const [error, setError] = useState('');
  const [lookupTripId, setLookupTripId] = useState('');
  const [recentTrips, setRecentTrips] = useState(() => getRecentTrips(user?.id));
  const showHome = location.state?.showHome === true;

  useEffect(() => {
    authApi.me().then(setMe).catch(() => {});
  }, []);

  useEffect(() => {
    let active = true;

    tripApi.getCurrent()
      .then(trip => {
        if (!active) return;

        setCurrentTrip(trip);
        if (trip && user?.id) {
          rememberTrip(user.id, trip);
          setRecentTrips(getRecentTrips(user.id));
        }
        if (!showHome) {
          navigate(trip ? `/rider/trips/${trip.id}` : '/rider/book', { replace: true });
        }
      })
      .catch(err => {
        if (active) setError(getErrorMessage(err));
      })
      .finally(() => {
        if (active) setChecking(false);
      });

    return () => {
      active = false;
    };
  }, [navigate, showHome, user?.id]);

  const handleLookupSubmit = e => {
    e.preventDefault();
    const trimmed = lookupTripId.trim();
    if (!trimmed) return;
    navigate(`/rider/trips/${trimmed}`);
  };

  return (
    <main>
      <h1>Rider — {user.name}</h1>
      <p>
        Mã hành khách: <code>{me?.id || user.id}</code>
        {me?.phone ? ` | SĐT: ${me.phone}` : ''}
      </p>

      {checking && <p>Đang kiểm tra chuyến đi hiện tại...</p>}
      {error && (
        <>
          <p role="alert">{error}</p>
          <button type="button" onClick={() => window.location.reload()}>Thử lại</button>
        </>
      )}

      <section>
        <h2>Chuyến đi hiện tại</h2>
        {currentTrip ? (
          <p>
            Mã chuyến: <code>{currentTrip.id}</code> — Trạng thái: <strong>{currentTrip.status}</strong>{' '}
            <Link to={`/rider/trips/${currentTrip.id}`}>Mở chuyến hiện tại</Link>
          </p>
        ) : (
          !checking && !error && (
            <p>
              Bạn hiện không có chuyến đang chạy.{' '}
              <Link to="/rider/book">Đặt chuyến xe mới</Link>
            </p>
          )
        )}
      </section>

      <section>
        <h2>Tra cứu chuyến đi theo Mã chuyến (Trip ID)</h2>
        <form onSubmit={handleLookupSubmit}>
          <label htmlFor="rider-lookup-trip">Mã chuyến (Trip ID) </label>
          <input
            id="rider-lookup-trip"
            type="text"
            size="40"
            placeholder="Nhập Trip ID để xem chi tiết / trạng thái hoàn tiền"
            value={lookupTripId}
            onChange={e => setLookupTripId(e.target.value)}
            required
          />{' '}
          <button type="submit">Xem chuyến</button>
        </form>

        {recentTrips.length > 0 && (
          <>
            <p>Các chuyến gần đây trên trình duyệt này:</p>
            <ul>
              {recentTrips.map(item => (
                <li key={item.id}>
                  <Link to={`/rider/trips/${item.id}`}><code>{item.id}</code></Link>
                  {item.pickupAddress && item.dropoffAddress
                    ? ` — ${item.pickupAddress} → ${item.dropoffAddress}`
                    : ''}
                  {item.status ? ` (${item.status})` : ''}
                </li>
              ))}
            </ul>
          </>
        )}
      </section>

      <p>
        <Link to="/rider/book">Đặt xe</Link>{' | '}
        <Link to="/wallet">Ví của tôi</Link>{' | '}
        <button type="button" onClick={logout}>Đăng xuất</button>
      </p>
    </main>
  );
}
