import { money } from '../utils/format';

export default function TripDetails({ trip }) {
  return <>
    <p>Trạng thái: <strong>{trip.status}</strong></p>
    <p>Điểm đón: {trip.pickup.address} ({trip.pickup.lat}, {trip.pickup.lng})</p>
    <p>Điểm đến: {trip.dropoff.address} ({trip.dropoff.lat}, {trip.dropoff.lng})</p>
    {trip.fare && <section>
      <h2>Giá chuyến</h2>
      <p>Giá: {money(trip.fare.price)}</p>
      <p>Quãng đường: {trip.fare.distanceKm} km</p>
      <p>Thời gian dự kiến: {trip.fare.durationMin} phút</p>
      <p>Thanh toán: {trip.fare.paymentMethod || 'Chưa chọn'}</p>
      <p>Chiến lược: {trip.fare.strategy}</p>
      <p>Trạng thái báo giá: {trip.fare.status}</p>
    </section>}
    <section>
      <h2>Tài xế</h2>
      {trip.driver ? <><p>{trip.driver.name}</p><p>Mã tài xế: {trip.driver.id}</p></>
        : <p>Chưa có tài xế nhận chuyến.</p>}
    </section>
    <section>
      <h2>Lịch sử trạng thái</h2>
      {trip.history?.length ? <ol>{trip.history.map((item, index) => <li key={`${item.to}-${item.at}-${index}`}>
        {item.from ? `${item.from} → ` : ''}{item.to}{' '}
        <time dateTime={item.at}>({new Date(item.at).toLocaleString('vi-VN')})</time>
      </li>)}</ol> : <p>Chưa có lịch sử.</p>}
    </section>
  </>;
}
