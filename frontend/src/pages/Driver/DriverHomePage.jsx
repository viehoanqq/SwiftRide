import { useCallback, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { tripApi } from '../../api/tripApi';
import { getErrorMessage } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';
import usePolling from '../../hooks/usePolling';
import { money } from '../../utils/format';

export default function DriverHomePage() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const [busy, setBusy] = useState('');
  const locked = useRef(false);
  const [actionError, setActionError] = useState('');
  const load = useCallback(async signal => {
    const [offers, current] = await Promise.all([
      tripApi.getDriverOffers(signal), tripApi.getDriverCurrent(signal),
    ]);
    return { offers, current };
  }, []);
  const { data, error, loading, refresh } = usePolling(load);
  const inProgress = ['DriverAccepted', 'EnRoute', 'PickedUp'].includes(data?.current?.status);

  const respond = async (id, action) => {
    if (locked.current) return;
    locked.current = true;
    setBusy(id);
    setActionError('');
    try {
      await tripApi.driverAction(id, action);
      if (action === 'driver-accept') navigate(`/driver/trips/${id}`);
    } catch (err) {
      setActionError(getErrorMessage(err));
    } finally {
      // Also reload after errors: Trip may have committed before Matching failed.
      await refresh();
      locked.current = false;
      setBusy('');
    }
  };

  return <main>
    <h1>Driver — {user.name}</h1>
    <p>Lời mời tự cập nhật mỗi 3 giây.</p>
    <button type="button" disabled={!!busy} onClick={refresh}>Tải lại</button>{' '}
    <button type="button" disabled={!!busy} onClick={logout}>Đăng xuất</button>
    {loading && <p>Đang tải...</p>}
    {error && <p role="alert">{error}</p>}
    {actionError && <p role="alert">{actionError}</p>}
    <section>
      <h2>Chuyến hiện tại</h2>
      {data?.current ? <p>{data.current.status}{' '}<Link to={`/driver/trips/${data.current.id}`}>Mở chuyến</Link></p>
        : !loading && !error && <p>Chưa có chuyến đã nhận.</p>}
    </section>
    <section>
      <h2>Lời mời chuyến</h2>
      {data?.offers.length === 0 && <p>Chưa có lời mời. Tài xế cần bật nhận chuyến và có vị trí để được ghép.</p>}
      {data?.offers.map(offer => <fieldset key={offer.offerId}>
        <legend>Chuyến {offer.tripId}</legend>
        <p>Điểm đón: {offer.pickup.address} ({offer.pickup.lat}, {offer.pickup.lng})</p>
        <p>Điểm đến: {offer.dropoff.address} ({offer.dropoff.lat}, {offer.dropoff.lng})</p>
        <p>Giá: {money(offer.price)}</p>
        <p>Khoảng cách đến điểm đón: {offer.distanceToPickupKm} km</p>
        <p>Lời mời lúc: {new Date(offer.createdAt).toLocaleString('vi-VN')}</p>
        <button type="button" disabled={!!busy || inProgress || !!error}
          onClick={() => respond(offer.tripId, 'driver-accept')}>Nhận</button>{' '}
        <button type="button" disabled={!!busy || !!error}
          onClick={() => respond(offer.tripId, 'driver-reject')}>Từ chối</button>{' '}
        <Link to={`/driver/trips/${offer.tripId}`}>Chi tiết</Link>
        {busy === offer.tripId && <p>Đang xử lý...</p>}
      </fieldset>)}
    </section>
  </main>;
}
