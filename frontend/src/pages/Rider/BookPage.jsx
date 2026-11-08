import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { tripApi } from '../../api/tripApi';
import { getErrorMessage } from '../../api/client';
import { places } from '../../data/places';

export default function BookPage() {
  const navigate = useNavigate();
  const [pickupId, setPickupId] = useState(places[0].id);
  const [dropoffId, setDropoffId] = useState(places[1].id);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleSubmit = async event => {
    event.preventDefault();
    setError('');

    if (pickupId === dropoffId) {
      setError('Điểm đón và điểm đến phải khác nhau.');
      return;
    }

    const pickup = places.find(place => place.id === pickupId);
    const dropoff = places.find(place => place.id === dropoffId);

    setLoading(true);
    try {
      const trip = await tripApi.create({
        pickup: { lat: pickup.lat, lng: pickup.lng, address: pickup.address },
        dropoff: { lat: dropoff.lat, lng: dropoff.lng, address: dropoff.address },
      });
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
        <div>
          <label htmlFor="pickup">Điểm đón</label>{' '}
          <select id="pickup" value={pickupId} onChange={event => setPickupId(event.target.value)}>
            {places.map(place => <option key={place.id} value={place.id}>{place.address}</option>)}
          </select>
        </div>
        <div>
          <label htmlFor="dropoff">Điểm đến</label>{' '}
          <select id="dropoff" value={dropoffId} onChange={event => setDropoffId(event.target.value)}>
            {places.map(place => <option key={place.id} value={place.id}>{place.address}</option>)}
          </select>
        </div>
        {error && <p role="alert">{error}</p>}
        <button type="submit" disabled={loading}>
          {loading ? 'Đang tìm báo giá...' : 'Đặt xe'}
        </button>
      </form>
      <p><Link to="/rider">Về trang Rider</Link></p>
    </main>
  );
}
