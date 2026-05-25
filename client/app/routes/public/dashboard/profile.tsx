import { useEffect } from "react";

export default function ProfilePage() {
	useEffect(() => {
		window.location.href = "http://stg-iam.blocksdevelopers.com/profile";
	}, []);

	return null;
}
