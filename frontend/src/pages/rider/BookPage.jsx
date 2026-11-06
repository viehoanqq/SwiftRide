
import { useState } from "react";
import { useNavigate } from "react-router-dom";
import client, { getErrorMessage } from "../../api/client";

export default function BookPage() {
    const navigate = useNavigate();

    const [pickupLocation, setPickupLocation] = useState("");
    const [destination, setDestination] = useState("");
    const [note, setNote] = useState("");

    const [loading, setLoading] = useState(false);
    const [error, setError] = useState("");

    async function handleSubmit(e) {
        e.preventDefault();

        setError("");

        // Kiểm tra dữ liệu đầu vào
        if (!pickupLocation.trim() || !destination.trim()) {
            setError("Vui lòng nhập đầy đủ điểm đón và điểm đến.");
            return;
        }

        if (pickupLocation.trim().toLowerCase() === destination.trim().toLowerCase()) {
            setError("Điểm đón và điểm đến không được giống nhau.");
            return;
        }

        try {
            setLoading(true);

            // Gửi yêu cầu đặt chuyến đến backend
            const response = await client.post("/trips", {
                pickupLocation: pickupLocation.trim(),
                destination: destination.trim(),
                note: note.trim()
            });

            const trip = response.data;

            // Chuyển đến trang chi tiết chuyến đi
            const tripId = trip.id ?? trip.tripId;

            if (!tripId) {
                throw new Error("Không tìm thấy mã chuyến đi trong phản hồi.");
            }

            navigate(`/rider/trips/${tripId}`);

        } catch (err) {
            setError(
                getErrorMessage
                    ? getErrorMessage(err)
                    : err.response?.data?.message || "Không thể đặt chuyến. Vui lòng thử lại."
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

                    <input
                        id="pickupLocation"
                        type="text"
                        placeholder="Nhập địa điểm đón"
                        value={pickupLocation}
                        onChange={(e) => setPickupLocation(e.target.value)}
                        disabled={loading}
                    />
                </div>

                <div>
                    <label htmlFor="destination">
                        Điểm đến
                    </label>

                    <input
                        id="destination"
                        type="text"
                        placeholder="Nhập địa điểm đến"
                        value={destination}
                        onChange={(e) => setDestination(e.target.value)}
                        disabled={loading}
                    />
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
