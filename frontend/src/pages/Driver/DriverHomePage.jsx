import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { tripApi } from '../../api/tripApi';
import { availabilityApi } from '../../api/availabilityApi';
import { authApi } from '../../api/authApi';
import { getErrorMessage } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';
import usePolling from '../../hooks/usePolling';
import { places } from '../../data/places';
import { money } from '../../utils/format';
import { getRecentTrips, rememberTrip } from '../../utils/recentTrips';

export default function DriverHomePage() {
  const { user, logout } = useAuth();
  const userId = user?.id;
  const navigate = useNavigate();
  const [me, setMe] = useState(null);
  const [busy, setBusy] = useState('');
  const locked = useRef(false);
  const [actionError, setActionError] = useState('');

  // Form bật/tắt nhận chuyến & tọa độ GPS
  const [placeId, setPlaceId] = useState(places[0].id);
  const [customLat, setCustomLat] = useState(String(places[0].lat));
  const [customLng, setCustomLng] = useState(String(places[0].lng));
  const [availSubmitting, setAvailSubmitting] = useState(false);
  const [availMessage, setAvailMessage] = useState('');

  // Form tra cứu chuyến theo ID
  const [lookupTripId, setLookupTripId] = useState('');

  useEffect(() => {
    authApi.me().then(setMe).catch(() => {});
  }, []);

  const load = useCallback(async signal => {
    const [offers, current, availability] = await Promise.all([
      tripApi.getDriverOffers(signal),
      tripApi.getDriverCurrent(signal),
      availabilityApi.getMe(signal),
    ]);
    if (current && userId) {
      rememberTrip(userId, current);
    }
    return { offers, current, availability };
  }, [userId]);

  const { data, error, loading, refresh } = usePolling(load);
  const recentTrips = getRecentTrips(userId);

  const inProgress = ['DriverAccepted', 'EnRoute', 'PickedUp'].includes(data?.current?.status);
  const driverStatus = data?.availability?.status;
  const isDriverBusy = driverStatus === 'Busy' || inProgress;

  const handleSetOnline = async e => {
    e.preventDefault();
    if (availSubmitting) return;
    setActionError('');
    setAvailMessage('');

    let lat;
    let lng;
    if (placeId === 'custom') {
      lat = Number(customLat);
      lng = Number(customLng);
    } else {
      const selected = places.find(p => p.id === placeId);
      lat = selected.lat;
      lng = selected.lng;
    }

    if (!Number.isFinite(lat) || !Number.isFinite(lng) || lat < -90 || lat > 90 || lng < -180 || lng > 180) {
      setActionError('Tọa độ không hợp lệ (-90 <= lat <= 90, -180 <= lng <= 180).');
      return;
    }

    setAvailSubmitting(true);
    try {
      await availabilityApi.setOnline(lat, lng);
      setAvailMessage(`Đã bật nhận chuyến tại tọa độ (${lat}, ${lng}).`);
      await refresh();
    } catch (err) {
      setActionError(getErrorMessage(err));
    } finally {
      setAvailSubmitting(false);
    }
  };

  const handleSetOffline = async () => {
    if (availSubmitting) return;
    setActionError('');
    setAvailMessage('');
    setAvailSubmitting(true);
    try {
      await availabilityApi.setOffline();
      setAvailMessage('Đã tắt trạng thái nhận chuyến (Offline).');
      await refresh();
    } catch (err) {
      setActionError(getErrorMessage(err));
    } finally {
      setAvailSubmitting(false);
    }
  };

  const respond = async (id, action) => {
    if (locked.current) return;
    locked.current = true;
    setBusy(id);
    setActionError('');
    setAvailMessage('');
    try {
      await tripApi.driverAction(id, action);
      if (action === 'driver-accept') {
        navigate(`/driver/trips/${id}`);
        return;
      }
    } catch (err) {
      setActionError(getErrorMessage(err));
    } finally {
      await refresh();
      locked.current = false;
      setBusy('');
    }
  };

  const handleLookupSubmit = e => {
    e.preventDefault();
    const trimmed = lookupTripId.trim();
    if (!trimmed) return;
    navigate(`/driver/trips/${trimmed}`);
  };

  return (
    <main>
      <h1>Driver — {user.name}</h1>
      <p>
        Mã tài xế: <code>{me?.id || user.id}</code>
        {me?.phone ? ` | SĐT: ${me.phone}` : ''}
      </p>
      <p>Lời mời và trạng thái trực tuyến tự cập nhật mỗi 3 giây.</p>
      <p>
        <Link to="/wallet">Ví của tôi</Link>{' | '}
        <button type="button" disabled={!!busy || availSubmitting} onClick={refresh}>Tải lại</button>{' '}
        <button type="button" disabled={!!busy || availSubmitting} onClick={logout}>Đăng xuất</button>
      </p>

      {loading && <p>Đang tải...</p>}
      {error && <p role="alert">{error}</p>}
      {actionError && <p role="alert">{actionError}</p>}
      {availMessage && <p>{availMessage}</p>}

      <section>
        <h2>Trạng thái nhận chuyến & Định vị (/availability)</h2>
        {data?.availability ? (
          <p>
            Trạng thái hiện tại: <strong>{data.availability.status}</strong>
            {data.availability.location && (
              <> — Vị trí: ({data.availability.location.lat}, {data.availability.location.lng})</>
            )}
            {data.availability.updatedAt && (
              <> — Cập nhật lúc: {new Date(data.availability.updatedAt).toLocaleString('vi-VN')}</>
            )}
          </p>
        ) : (
          !loading && <p>Trạng thái hiện tại: <strong>Chưa bật nhận chuyến lần nào</strong></p>
        )}

        <form onSubmit={handleSetOnline}>
          <fieldset disabled={availSubmitting || isDriverBusy}>
            <legend>Bật / Tắt nhận chuyến</legend>
            <div>
              <label htmlFor="driver-place">Vị trí đứng đón </label>
              <select
                id="driver-place"
                value={placeId}
                onChange={e => setPlaceId(e.target.value)}
              >
                {places.map(place => (
                  <option key={place.id} value={place.id}>
                    {place.address} ({place.lat}, {place.lng})
                  </option>
                ))}
                <option value="custom">-- Tự nhập tọa độ GPS --</option>
              </select>
            </div>

            {placeId === 'custom' && (
              <div>
                <label>
                  Vĩ độ (Lat){' '}
                  <input
                    type="number"
                    step="any"
                    value={customLat}
                    onChange={e => setCustomLat(e.target.value)}
                    required
                  />
                </label>{' '}
                <label>
                  Kinh độ (Lng){' '}
                  <input
                    type="number"
                    step="any"
                    value={customLng}
                    onChange={e => setCustomLng(e.target.value)}
                    required
                  />
                </label>
              </div>
            )}

            <p>
              <button type="submit" disabled={availSubmitting || isDriverBusy}>
                {driverStatus === 'Available' ? 'Cập nhật vị trí Online' : 'Bật nhận chuyến (Online)'}
              </button>{' '}
              <button
                type="button"
                disabled={availSubmitting || isDriverBusy || !data?.availability || driverStatus === 'Offline'}
                onClick={handleSetOffline}
              >
                Tắt nhận chuyến (Offline)
              </button>
            </p>
            {isDriverBusy && <p>Bạn đang thực hiện chuyến đi (Busy), không thể đổi trạng thái Online/Offline.</p>}
          </fieldset>
        </form>
      </section>

      <section>
        <h2>Chuyến hiện tại</h2>
        {data?.current ? (
          <p>
            Mã chuyến: <code>{data.current.id}</code> — Trạng thái: <strong>{data.current.status}</strong>{' '}
            <Link to={`/driver/trips/${data.current.id}`}>Mở chuyến</Link>
          </p>
        ) : (
          !loading && !error && <p>Chưa có chuyến đang thực hiện.</p>
        )}
      </section>

      <section>
        <h2>Lời mời chuyến ({data?.offers?.length ?? 0})</h2>
        {data?.offers?.length === 0 && (
          <p>Chưa có lời mời. Tài xế cần bật nhận chuyến (Available) và ở gần điểm đón để được ghép.</p>
        )}
        {data?.offers?.map(offer => (
          <fieldset key={offer.offerId}>
            <legend>Chuyến {offer.tripId}</legend>
            <p>Điểm đón: {offer.pickup.address} ({offer.pickup.lat}, {offer.pickup.lng})</p>
            <p>Điểm đến: {offer.dropoff.address} ({offer.dropoff.lat}, {offer.dropoff.lng})</p>
            <p>Giá cước: <strong>{money(offer.price)}</strong></p>
            <p>Khoảng cách đến điểm đón: {offer.distanceToPickupKm} km</p>
            <p>Lời mời lúc: {new Date(offer.createdAt).toLocaleString('vi-VN')}</p>
            <button
              type="button"
              disabled={!!busy || inProgress || !!error}
              onClick={() => respond(offer.tripId, 'driver-accept')}
            >
              Nhận chuyến
            </button>{' '}
            <button
              type="button"
              disabled={!!busy || !!error}
              onClick={() => respond(offer.tripId, 'driver-reject')}
            >
              Từ chối
            </button>{' '}
            <Link to={`/driver/trips/${offer.tripId}`}>Chi tiết</Link>
            {busy === offer.tripId && <p>Đang xử lý...</p>}
          </fieldset>
        ))}
      </section>

      <section>
        <h2>Tra cứu chuyến đi đã thực hiện</h2>
        <form onSubmit={handleLookupSubmit}>
          <label htmlFor="driver-lookup-trip">Mã chuyến (Trip ID) </label>
          <input
            id="driver-lookup-trip"
            type="text"
            size="40"
            placeholder="Nhập Trip ID để xem kết quả thanh toán / hoàn tiền"
            value={lookupTripId}
            onChange={e => setLookupTripId(e.target.value)}
            required
          />{' '}
          <button type="submit">Mở chuyến</button>
        </form>

        {recentTrips.length > 0 && (
          <>
            <p>Các chuyến gần đây trên trình duyệt này:</p>
            <ul>
              {recentTrips.map(item => (
                <li key={item.id}>
                  <Link to={`/driver/trips/${item.id}`}><code>{item.id}</code></Link>
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
    </main>
  );
}
