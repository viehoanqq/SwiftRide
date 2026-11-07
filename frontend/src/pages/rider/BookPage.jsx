import { useState } from "react";
import { useNavigate } from "react-router-dom";
import client, { getErrorMessage } from "../../api/client";

// Danh sách điểm đón
const pickupLocations = [
    {
        lat: 10.772,
        lng: 106.699,
        address: "123 Lê Lợi, Q1"
    }
];

// Danh sách điểm đến
const destinationLocations = [
    {
        lat: 10.801,
        lng: 106.75,
        address: "456 Trần Hưng Đạo, Q5"
    }
];

export default function BookPage() {
    const navigate = useNavigate();

    const [pickupLocation, setPickupLocation] = useState("");
    const [destination, setDestination] = useState("");

    const [loading, setLoading] = useState(false);
    const [error, setError] = useState("");

    async function handleSubmit(e) {
        e.preventDefault();
        setError("");

        if (!pickupLocation || !destination) {
            setError("Vui lòng chọn đầy đủ điểm đón và điểm đến.");
            return;
        }

        // Chuyển dữ liệu JSON từ select thành object
        const pickup = JSON.parse(pickupLocation);
        const dropoff = JSON.parse(destination);

        if (pickup.lat === dropoff.lat && pickup.lng === dropoff.lng) {
            setError("Điểm đón và điểm đến không được giống nhau.");
            return;
        }

        try {
            setLoading(true);

            // Gửi đúng cấu trúc CreateTripRequest của backend
            const response = await client.post("/trips", {
                pickup,
                dropoff
            });

            const trip = response.data;
            const tripId = trip.tripId;

            if (!tripId) {
                throw new Error("Không tìm thấy mã chuyến đi trong phản hồi.");
            }

            navigate(`/rider/trips/${tripId}`);

        } catch (err) {
            setError(
                err.response
                    ? getErrorMessage(err)
                    : err.message || "Không thể đặt chuyến. Vui lòng thử lại."
            );
        } finally {
            setLoading(false);
        }
    }

    return (
        <div>
            <h1>Đặt chuyến xe</h1>
            <p>Nhập thông tin chuyến đi của bạn.</p>

            <form onSubmit={handleSubmit}>
                <div>
                    <label htmlFor="pickupLocation">
                        Điểm đón
                    </label>

                    <select
                        id="pickupLocation"
                        value={pickupLocation}
                        onChange={(e) => setPickupLocation(e.target.value)}
                        disabled={loading}
                    >
                        <option value="">Chọn điểm đón</option>

                        {pickupLocations.map((location, index) => (
                            <option
                                key={index}
                                value={JSON.stringify(location)}
                            >
                                {location.address}
                            </option>
                        ))}
                    </select>
                </div>

                <div>
                    <label htmlFor="destination">
                        Điểm đến
                    </label>

                    <select
                        id="destination"
                        value={destination}
                        onChange={(e) => setDestination(e.target.value)}
                        disabled={loading}
                    >
                        <option value="">Chọn điểm đến</option>

                        {destinationLocations.map((location, index) => (
                            <option
                                key={index}
                                value={JSON.stringify(location)}
                            >
                                {location.address}
                            </option>
                        ))}
                    </select>
                </div>

                {error && (
                    <p role="alert">
                        {error}
                    </p>
                )}

                <button type="submit" disabled={loading}>
                    {loading ? "Đang đặt chuyến..." : "Đặt xe"}
                </button>
            </form>
        </div>
    );
}