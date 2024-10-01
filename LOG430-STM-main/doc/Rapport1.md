# Laboratoire #1

Groupe: 02

Equipe: 04

Membres de l'équipe: Ali Dickens Augustin, Jean-Philippe Lalonde, Jonathan Rodriguez Tames et Alexandre Roy

# Évaluation de la participation

> L'évaluation suivante est faite afin d'encourager des discussions au sein de l'équipe. Une discussion saine du travail de chacun est utile afin d'améliorer le climat de travail. Les membres de l'équipe ont le droit de retirer le nom d'un ou une collègue du rapport.
> |nom de l'étudiant| Facteur multiplicatif|
> |:---------------:|:--------------------:|
> |Ali Dickens Augustin | 1 |
> |Jean-Philippe Lalonde | 1 |
> |Jonathan Rodriguez Tames| 1 |
> |Alexandre Roy | 1 |

# Introduction /2

> Dans ce laboratoire, l'objectif principal est de comprendre et mettre en œuvre les concepts de microservice du projet LOG430-STM. De plus, chaque microservice joue un rôle distinct dans le but d'interagir afin de fournir des fonctionnalités telles que la collecte, le traitement et la comparaison de données liés au transport en commun des bus de la STM, et ce, en temps réel. L'architecture du projet implique entre autres une série de conteneurs Docker, qui doivent être déployés ainsi que configurés correctement dans le but de garantir la communication efficace entre les différents microservices. Ainsi, l'équipe est amenée à explorer ensemble diverses tâches, dont la configuration initiale du système, de l'exécution de microservices sur Docker, d'interagir avec des API externes et de manipuler des données réelles, et ce, en passant par l'optimisation des performances grâce à l'ajout de bases de données, l'ajout d'un ping echo et donc à la mise en œuvre de tactiques de résilience.

# Diagramme de séquence de haut niveau

## Explication générale du fonctionnement du système /2

> - **NodeController** : Ce dernier est considéré comme étant le chef d'orchestre des tests de chaos, assurant ainsi la perturbation contrôlée des microservices afin de tester entre autres leur résilience. Ensuite, celui-ci a pour but de surveiller la durée de vie des différents conteneurs Docker et de fournir des mécanismes pour gérer, équilibrer ou même créer la charge de ces derniers. Ainsi, le NodeController a pour but de faciliter la connexion aux serveurs de l'ÉTS ce qui permet donc de communiquer avec le restant du système et son API permet d'ajuster dynamiquement les différents services en cours d'exécution. Finalement, son équilibrage de charge utilise deux stratégies étant le Round Robin étant décrit comme distributeur de requêtes de manière aléatoire, et ce, parmi les instances disponibles. Le second étant Broadcast, qui lui envoie plutôt la requête à toutes les instances.
- **STM** : Le microservice STM agit de son côté comme étant un adaptateur pour l'API de la Société de Transport de Montréal (STM). Ce dernier fournit entre autres des fonctionnalités supplémentaires par rapport à l'API originale, dont l'obtention de données à intervalles réguliers (50 ms) et le suivi en temps réel des bus entre deux coordonnées spécifiques. Le STM est basé sur une architecture en couche, respectant donc le principe d'inversion des dépendances dans le but de faciliter toutes modifications et de maximiser la modularité. Ainsi, afin d'améliorer les performances de ce service, l'équipe doit donc configurer une base de données afin que celui-ci stocke ses données statiques GTFS dans cette dernière (PostgreSQL) plutôt que de la conserver en mémoire ce qui réduit alors l'utilisation de la RAM.
- **RouteTimeProvider** : Le microservice RouteTimeProvider a pour but de fournir des informations sur le temps de trajet basé sur les routes et les horaires disponibles pour les différents bus de la STM ou voitures, et ce, grâce à son utilisation de l'API TomTom. Au début, ce microservice n'est pas fonctionnel dans le projet et donc la création d'un fichier Dockerfile est obligatoire afin de rendre ce dernier opérationnel permettant ainsi la possibilité d'exécuter ses différentes fonctions. De plus, lorsque ce dernier configuré selon les normes du projet, il exposera une route nommée ping echo ayant pour but simple de vérifier son état de fonctionnement tous les 0.5 seconde, et ce, grâce à son instance. Si cette dernière est active, la route transmettra un message isAlive, permettant aux autres microservices de s'assurer qu'il est bien fonctionnel.
- **TripComparator** : Finalement, le TripComparator a pour mission de comparer les différentes options de trajet. Son fonctionnement est qu'il surveille en permanence l'état du RouteTimeProvider afin de vérifier sa disponibilité grâce à l'envoi du ping echo configuré préalablement. De plus, si le microservice RouteTimeProvider est redéployé ou bien détruit, ce dernier va détecter ce changement et donc identifier un nouveau port sur lequel l'instance sera possible. Ainsi, cette fonctionnalité de surveillance continue assure la réactivité et le fonctionnement opérationnel du système le rendant ainsi plus dynamique aux différents changements.

## Diagramme de séquence /3

> TODO: Insérer le diagramme de séquence de haut niveau pour illustrer ce qui arrive lorsqu'une requête de comparaison de trajet provenant du dashboard arrive au système.

# Diagrammes de contexte de haut niveau

## Microservice STM /3

> TODO: Insérer le diagramme de contexte de haut niveau pour le microservice STM.

## Microservice RouteTimeProvider /3

> TODO: Insérer le diagramme de contexte de haut niveau pour le microservice RouteTimeProvider.

## Microservice TripComparator /3

> TODO: Insérer le diagramme de contexte de haut niveau pour le microservice TripComparator.

# Questions générales /12

1. L'ajout de la base de donnés est une amélioration au système. Quel attribut de qualité est amélioré par l'ajout d'une base de données? Expliquez votre réponse. **/3**

Tout d'abord, l'ajout d'une base de données a pour d'améliorer plusieurs attributs de qualité dans un système en général. Dans notre cas, l'attribut de qualité ayant été amélioré lors de ce projet est principalement la performance. Celle-ci mesure entre autres la rapidité ainsi que l'efficacité avec laquelle le système va répondre aux différentes requêtes et traiter les données stockées. Dans le cadre du laboratoire, l'équipe a utilisé DBeaver afin d'ajouter une base de données PostgreSQL permettant d'optimiser l'interaction avec le microservice STM favorisant comme dit plutôt la performance. Ainsi, cette dernière permet de réduire les temps de réponse, et ce, en remplaçant la gestion en mémoire des données par une base de données optimisée, ce qui permet alors de récupérer les données GTFS plus rapidement. De plus, la base de données que l'équipe doit gérer est énorme et contient des millions de données provenant de la STM et donc une base de données comme PostgreSQL est plus optimale pour cela puisqu'elle est conçue de manière à gérer de grandes quantités de données, et ce, efficacement ce qui accélère donc le traitement des nombreuses requêtes de suivi de bus. Le temps de traitement entre les différents microservices dont l'envoi d'une requête de TripComparator et la réponse reçu par STM est ainsi réduit. Ensuite, l'instauration de la base de données aide aussi à l'optimisation des ressources système permettant un accès sélectif aux données pertinentes, évitant donc de charger toutes les données GTFS statiques en mémoire ce qui pourrait augmenter considérablement la RAM et entraîner des ralentissements dans le traitement de celles-ci. Ainsi, la charge sur le système sera diminuée, ce qui va permettre et aider une exécution plus fluide des autres microservices. Une autre amélioration serait au niveau de la scalabilité comme vue dans le cours. L'implémentation d'une base de données rend plus facile de gérer un grand nombre de requêtes en simultanées puisque PostgreSQL permet avec efficacité ce traitement de plusieurs connexions.

2. Lorsqu'une requête de comparaison du temps de trajet faite sur le Dashboard est envoyée au NodeController, différentes requêtes sont échangées sur le réseau. Pendant une minute, combien de requêtes sont échangées entre les microservices **NodeController**, **TripComparator**, **RouteTimeProvider** et **STM** et avec les API externes de la STM et de TomTom ? Décrivez également rapidement les différentes requêtes échangées. **/4**

2.1 Dans le code source du projet, nous pouvons ajuster le taux de lancement des requêtes. Expliquez comment nous pouvons nous y prendre pour ce faire en donnant un ou des extraits spécifiques de code. **/2**

3. Proposez une tactique permettant d'améliorer la disponibilité de l'application lors d'une attaque des conteneurs de computation (**TripComparator**, **RouteTimeProvider**, et **STM**) lors du 2e laboratoire. **/3**

# Conclusion du laboratoire /2

> En conclusion, ce projet offre une expérience pratique permettant davantage la compréhension du déploiement et la gestion des microservices. De plus, en travaillant sur la performance du système ainsi que l'amélioration de la disponibilité, l'équipe a acquis plusieurs connaissances et différentes compétences notamment en configuration Docker, en surveillance de l'état des services par la configuration entre autres du ping echo et en intégration de bases de données. Finalement, le travail effectué sur les microservices sur la comparaison de trajets en temps réel a permis de développer une certaine expertise en ce qui concerne le fait de maintenir et concevoir des systèmes complexes, et ce, basés sur une architecture de microservices.


- Créer un tag git avec la commande "git tag laboratoire-1"