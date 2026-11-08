import { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext';
import { tripApi } from '../../api/tripApi';
import { getErrorMessage } from '../../api/client';

export default function RiderHomePage() {
  const { user, logout } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const [currentTrip, setCurrentTrip] = useState(null);
  const [checking, setChecking] = useState(true);
  const [error, setError] = useState('');
  const showHome = location.state?.showHome === true;

  useEffect(() => {
    let active = true;

    tripApi.getCurrent()
      .then(trip => {
        if (!active) return;

        setCurrentTrip(trip);
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
  }, [navigate, showHome]);

  return (
    <main>
      <h1>Xin chào, {user.name}</h1>
      {checking && <p>Đang kiểm tra chuyến đi hiện tại...</p>}
      {error && (
        <>
          <p role="alert">{error}</p>
          <button type="button" onClick={() => window.location.reload()}>Thử lại</button>
        </>
      )}
      {currentTrip
        ? (
          <p>
            Chuyến hiện tại: {currentTrip.status}{' '}
            <Link to={`/rider/trips/${currentTrip.id}`}>Mở chuyến</Link>
          </p>
        )
        : !checking && !error && <p><Link to="/rider/book">Đặt chuyến xe</Link></p>}
      <button type="button" onClick={logout}>Đăng xuất</button>
    </main>
  );
}
