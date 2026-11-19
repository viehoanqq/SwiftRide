import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { tripApi } from '../../api/tripApi';
import { getErrorMessage } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';
import { places } from '../../data/places';
import { rememberTrip } from '../../utils/recentTrips';

export default function BookPage() {
  const navigate = useNavigate();
  const { user } = useAuth();

  const [pickupId, setPickupId] = useState(places[0].id);
  const [customPickup, setCustomPickup] = useState({
    address: '',
    lat: String(places[0].lat),
    lng: String(places[0].lng),
  });

  const [dropoffId, setDropoffId] = useState(places[1].id);
  const [customDropoff, setCustomDropoff] = useState({
    address: '',
    lat: String(places[1].lat),
    lng: String(places[1].lng),
  });

  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const resolveLocation = (selectedId, custom) => {
    if (selectedId === 'custom') {
      return {
        address: custom.address.trim(),
        lat: Number(custom.lat),
        lng: Number(custom.lng),
      };
    }
    const found = places.find(place => place.id === selectedId);
    return { address: found.address, lat: found.lat, lng: found.lng };
  };

  const handleSubmit = async event => {
    event.preventDefault();
    setError('');

    const pickup = resolveLocation(pickupId, customPickup);
    const dropoff = resolveLocation(dropoffId, customDropoff);

    if (!pickup.address || !dropoff.address) {
      setError('Vui lòng nhập đầy đủ địa chỉ điểm đón và điểm đến.');
      return;
    }

    if (
      !Number.isFinite(pickup.lat) || !Number.isFinite(pickup.lng) ||
      pickup.lat < -90 || pickup.lat > 90 || pickup.lng < -180 || pickup.lng > 180 ||
      !Number.isFinite(dropoff.lat) || !Number.isFinite(dropoff.lng) ||
      dropoff.lat < -90 || dropoff.lat > 90 || dropoff.lng < -180 || dropoff.lng > 180
    ) {
      setError('Tọa độ không hợp lệ (-90 <= lat <= 90, -180 <= lng <= 180).');
      return;
    }

    if (pickup.lat === dropoff.lat && pickup.lng === dropoff.lng) {
      setError('Điểm đón và điểm đến phải có tọa độ khác nhau.');
      return;
    }

    setLoading(true);
    try {
      const trip = await tripApi.create({ pickup, dropoff });
      if (user?.id && trip?.tripId) {
        rememberTrip(user.id, {
          id: trip.tripId,
          status: trip.status,
          pickup,
          dropoff,
        });
      }
      navigate(`/rider/trips/${trip.tripId}`);
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setLoading(false);
    }
  };

  return (
    <main>
      <h1>Đặt xe</h1>
      <form onSubmit={handleSubmit}>
        <fieldset disabled={loading}>
          <legend>Điểm đón (Pickup)</legend>
          <div>
            <label htmlFor="pickup">Chọn địa điểm </label>
            <select
              id="pickup"
              value={pickupId}
              onChange={event => setPickupId(event.target.value)}
            >
              {places.map(place => (
                <option key={place.id} value={place.id}>
                  {place.address} ({place.lat}, {place.lng})
                </option>
              ))}
              <option value="custom">-- Tự nhập địa chỉ & tọa độ --</option>
            </select>
          </div>
          {pickupId === 'custom' && (
            <div>
              <div>
                <label>
                  Địa chỉ đón{' '}
                  <input
                    type="text"
                    size="35"
                    value={customPickup.address}
                    onChange={e => setCustomPickup({ ...customPickup, address: e.target.value })}
                    required
                  />
                </label>
              </div>
              <div>
                <label>
                  Vĩ độ (Lat){' '}
                  <input
                    type="number"
                    step="any"
                    value={customPickup.lat}
                    onChange={e => setCustomPickup({ ...customPickup, lat: e.target.value })}
                    required
                  />
                </label>{' '}
                <label>
                  Kinh độ (Lng){' '}
                  <input
                    type="number"
                    step="any"
                    value={customPickup.lng}
                    onChange={e => setCustomPickup({ ...customPickup, lng: e.target.value })}
                    required
                  />
                </label>
              </div>
            </div>
          )}
        </fieldset>

        <fieldset disabled={loading}>
          <legend>Điểm đến (Dropoff)</legend>
          <div>
            <label htmlFor="dropoff">Chọn địa điểm </label>
            <select
              id="dropoff"
              value={dropoffId}
              onChange={event => setDropoffId(event.target.value)}
            >
              {places.map(place => (
                <option key={place.id} value={place.id}>
                  {place.address} ({place.lat}, {place.lng})
                </option>
              ))}
              <option value="custom">-- Tự nhập địa chỉ & tọa độ --</option>
            </select>
          </div>
          {dropoffId === 'custom' && (
            <div>
              <div>
                <label>
                  Địa chỉ đến{' '}
                  <input
                    type="text"
                    size="35"
                    value={customDropoff.address}
                    onChange={e => setCustomDropoff({ ...customDropoff, address: e.target.value })}
                    required
                  />
                </label>
              </div>
              <div>
                <label>
                  Vĩ độ (Lat){' '}
                  <input
                    type="number"
                    step="any"
                    value={customDropoff.lat}
                    onChange={e => setCustomDropoff({ ...customDropoff, lat: e.target.value })}
                    required
                  />
                </label>{' '}
                <label>
                  Kinh độ (Lng){' '}
                  <input
                    type="number"
                    step="any"
                    value={customDropoff.lng}
                    onChange={e => setCustomDropoff({ ...customDropoff, lng: e.target.value })}
                    required
                  />
                </label>
              </div>
            </div>
          )}
        </fieldset>

        {error && <p role="alert">{error}</p>}
        <p>
          <button type="submit" disabled={loading}>
            {loading ? 'Đang tìm báo giá...' : 'Đặt xe & Nhận báo giá'}
          </button>
        </p>
      </form>

      <p>
        <Link to="/wallet">Ví của tôi</Link>{' | '}
        <Link to="/rider" state={{ showHome: true }}>Về trang Rider</Link>
      </p>
    </main>
  );
}
