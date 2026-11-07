
import { useState } from "react";

export default function TripQuote({ trip, onConfirm, onReject }) {
    const [paymentMethod, setPaymentMethod] = useState("wallet");

    function handleSubmit(e) {
        e.preventDefault();
        if (onConfirm) {
            onConfirm(paymentMethod);
        }
    }

    return (
        <div>
            <h2>Báo giá chuyến đi</h2>

            <p>Mã chuyến: {trip.id}</p>
            <p>Điểm đón: {trip.pickup.address}</p>
            <p>Điểm đến: {trip.dropoff.address}</p>
            <p>Giá: {trip.price}</p>
            <p>Quãng đường: {trip.distance}</p>
            <p>Thời gian dự kiến: {trip.estimatedDuration}</p>
            <p>Chiến lược ghép chuyến: {trip.strategy}</p>

            <form onSubmit={handleSubmit}>
                <h3>Chọn phương thức thanh toán</h3>

                <label>
                    <input
                        type="radio"
                        name="payment"
                        value="wallet"
                        checked={paymentMethod === "wallet"}
                        onChange={(e) => setPaymentMethod(e.target.value)}
                    />
                    Ví
                </label>

                <br />

                <label>
                    <input
                        type="radio"
                        name="payment"
                        value="card"
                        checked={paymentMethod === "card"}
                        onChange={(e) => setPaymentMethod(e.target.value)}
                    />
                    Thẻ
                </label>

                <br />

                <button type="submit">
                    Đồng ý
                </button>

                <button
                    type="button"
                    onClick={onReject}
                >
                    Từ chối
                </button>
            </form>
        </div>
    );
}
