import { useState } from "react";
import { useNavigate } from "react-router-dom";
import TripQuote from "./TripQuote";
import TripTimeline from "./TripTimeline";

export default function TripPage() {
    const navigate = useNavigate();

    const [trip, setTrip] = useState({
        id: 101,
        price: 150000,
        distance: "5 km",
        estimatedDuration: "15 phút",
        strategy: "Ghép chuyến",
        status: "Quoted",
        pickup: {
            lat: 10.772,
            lng: 106.699,
            address: "123 Lê Lợi, Q1"
        },
        dropoff: {
            lat: 10.801,
            lng: 106.75,
            address: "456 Trần Hưng Đạo, Q5"
        }
    });

    function handleConfirm(paymentMethod) {
        console.log("Phương thức thanh toán:", paymentMethod);

        // Cập nhật trạng thái bằng React state
        setTrip(prev => ({
            ...prev,
            status: "Accepted"
        }));
    }

    function handleReject() {
        navigate("/rider/book");
    }

    if (trip.status === "Quoted") {
        return (
            <TripQuote
                trip={trip}
                onConfirm={handleConfirm}
                onReject={handleReject}
            />
        );
    }

    return <TripTimeline trip={trip} />;
}